using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Models
{
	internal sealed class DaiNaiShipEntry
	{
		public int ShipId { get; set; }
		public bool Daih { get; set; }
		public bool Naik { get; set; }
		public string Name { get; set; }
	}

	internal static class DaiNaiShipProvider
	{
		private const int MaxResponseSizeBytes = 1 * 1024 * 1024;
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
		private static IReadOnlyDictionary<int, DaiNaiShipEntry> _entries;

		public static IReadOnlyDictionary<int, DaiNaiShipEntry> Entries
		{
			get
			{
				if (_entries == null)
					_entries = Load();
				return _entries;
			}
		}

		internal static async Task UpdateLocalFileAsync()
		{
			var source = Properties.Settings.Default.DaiNaiShipSource;
			if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
			{
				Debug.WriteLine("DaiNaiShipProvider.UpdateLocalFileAsync: URI が無効のため更新をスキップします。");
				return;
			}

			try
			{
				using (var client = new HttpClient(Helper.GetProxyConfiguredHandler()))
				{
					client.Timeout = RequestTimeout;
					client.MaxResponseContentBufferSize = MaxResponseSizeBytes;

					using (var response = await client.GetAsync(uri).ConfigureAwait(false))
					{
						if (!response.IsSuccessStatusCode)
						{
							Debug.WriteLine(
								"DaiNaiShipProvider.UpdateLocalFileAsync: HTTP失敗 "
								+ (int)response.StatusCode
								+ " "
								+ response.ReasonPhrase);
							return;
						}

						var remoteJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
						if (!IsValidDaiNaiShipJson(remoteJson))
						{
							Debug.WriteLine("DaiNaiShipProvider.UpdateLocalFileAsync: サーバーJSONが不正のため更新をスキップします。");
							return;
						}

						var savePath = GetSaveFilePath();
			var existingLocalPath = GetExistingLocalFilePath();
			var localJson = ReadLocalFile(existingLocalPath);
			var remoteVersion = GetVersion(remoteJson);
			var localVersion = GetVersion(localJson);

			if (existingLocalPath != null && remoteVersion <= localVersion)
			{
				Debug.WriteLine(
					"DaiNaiShipProvider.UpdateLocalFileAsync: 更新不要です。"
					+ " local=" + localVersion
					+ ", remote=" + remoteVersion);
				return;
			}

			WriteLocalFileAtomically(savePath, remoteJson);
			_entries = null;

			Debug.WriteLine(
							"DaiNaiShipProvider.UpdateLocalFileAsync: DaiNai_Ship.json を更新しました。"
							+ " version=" + remoteVersion);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Debug.WriteLine("DaiNaiShipProvider.UpdateLocalFileAsync: 通信失敗: " + ex.Message);
			}
			catch (TaskCanceledException ex)
			{
				Debug.WriteLine("DaiNaiShipProvider.UpdateLocalFileAsync: タイムアウト: " + ex.Message);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("DaiNaiShipProvider.UpdateLocalFileAsync: 更新失敗: " + ex);
			}
		}

		public static IEnumerable<int> GetShipIds(Func<DaiNaiShipEntry, bool> predicate)
		{
			if (predicate == null)
				return Enumerable.Empty<int>();

			return Entries.Values
				.Where(predicate)
				.Select(x => x.ShipId);
		}

		private static IReadOnlyDictionary<int, DaiNaiShipEntry> Load()
		{
			try
			{
				var path = GetLoadFilePaths().FirstOrDefault(File.Exists);
				if (path == null)
					return new Dictionary<int, DaiNaiShipEntry>();

				var root = JObject.Parse(File.ReadAllText(path));
				var ships = root["DaiNaiShip"] as JObject;
				if (ships == null)
					return new Dictionary<int, DaiNaiShipEntry>();

				var result = new Dictionary<int, DaiNaiShipEntry>();
				foreach (var p in ships.Properties())
				{
					if (!int.TryParse(p.Name, out var id)) continue;
					var obj = p.Value as JObject;
					if (obj == null) continue;

					result[id] = new DaiNaiShipEntry
					{
						ShipId = id,
						Daih = ToBool(obj["Daih"]),
						Naik = ToBool(obj["Naik"]),
						Name = obj["name"]?.Value<string>() ?? ""
					};
				}
				return result;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("DaiNaiShipProvider.Load: 読込失敗: " + ex);
				return new Dictionary<int, DaiNaiShipEntry>();
			}
		}

		private static string GetExecutableDirectory()
		{
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;

			return Path.GetDirectoryName(executablePath)
				?? AppDomain.CurrentDomain.BaseDirectory;
		}

		private static string[] GetLoadFilePaths()
		{
			var dir = GetExecutableDirectory();

			return new[]
			{
				Path.Combine(dir, "json", "DaiNai_Ship.json"),
				Path.Combine(dir, "DaiNai_Ship.json"),
			};
		}

		private static string GetExistingLocalFilePath()
		{
			return GetLoadFilePaths().FirstOrDefault(File.Exists);
		}

		private static string GetSaveFilePath()
		{
			return Path.Combine(GetExecutableDirectory(), "json", "DaiNai_Ship.json");
		}

		private static string ReadLocalFile(string localPath)
		{
			if (!File.Exists(localPath))
			{
				return null;
			}

			return File.ReadAllText(localPath, Encoding.UTF8);
		}

		private static long GetVersion(string content)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(content))
				{
					return 0;
				}

				var root = JObject.Parse(content);
				var version = root["version"]?.Value<long?>();

				return version.GetValueOrDefault() > 0
					? version.Value
					: 0;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("DaiNaiShipProvider.GetVersion: バージョン取得失敗: " + ex.Message);
				return 0;
			}
		}

		private static bool IsValidDaiNaiShipJson(string content)
		{
			try
			{
				var root = JObject.Parse(content);
				var ships = root["DaiNaiShip"] as JObject;

				return ships != null && ships.HasValues;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("DaiNaiShipProvider.IsValidDaiNaiShipJson: JSON検証失敗: " + ex.Message);
				return false;
			}
		}

		private static void WriteLocalFileAtomically(string localPath, string content)
		{
			var directory = Path.GetDirectoryName(localPath);
			if (string.IsNullOrEmpty(directory))
			{
				throw new InvalidOperationException("DaiNai_Ship.json の保存先ディレクトリを取得できません。");
			}

			Directory.CreateDirectory(directory);

			var temporaryPath = localPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

			try
			{
				File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));

				if (File.Exists(localPath))
				{
					File.Replace(temporaryPath, localPath, null);
				}
				else
				{
					File.Move(temporaryPath, localPath);
				}
			}
			finally
			{
				if (File.Exists(temporaryPath))
				{
					File.Delete(temporaryPath);
				}
			}
		}

		private static bool ToBool(JToken token)
		{
			if (token == null) return false;
			if (int.TryParse(token.ToString(), out var i)) return i != 0;
			return string.Equals(token.ToString(), "true", StringComparison.OrdinalIgnoreCase);
		}
	}
}
