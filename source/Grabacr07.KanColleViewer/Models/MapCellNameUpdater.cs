using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Grabacr07.KanColleViewer.Models
{
	/// <summary>
	/// MapCellNames.json の起動時更新を行います。
	/// </summary>
	internal static class MapCellNameUpdater
	{
		private const int MaxResponseSizeBytes = 1 * 1024 * 1024;
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

		/// <summary>
		/// サーバー上の MapCellNames.json がローカルより新しい場合だけ、
		/// ローカルファイルを更新します。
		/// </summary>
		public static async Task UpdateLocalFileAsync()
		{
			var source = Properties.Settings.Default.MapCellNameSource;
			if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
			{
				Debug.WriteLine("MapCellNameUpdater: URI が無効のため更新をスキップします。");
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
								"MapCellNameUpdater: HTTP失敗 "
								+ (int)response.StatusCode
								+ " "
								+ response.ReasonPhrase);
							return;
						}

						var remoteJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
						if (!IsValidMapCellNamesJson(remoteJson))
						{
							Debug.WriteLine("MapCellNameUpdater: サーバーJSONが不正のため更新をスキップします。");
							return;
						}

						var localPath = GetLocalFilePath();
						var localJson = ReadLocalFile(localPath);

						var remoteVersion = GetVersion(remoteJson);
						var localVersion = GetVersion(localJson);

						if (File.Exists(localPath) && remoteVersion <= localVersion)
						{
							Debug.WriteLine(
								"MapCellNameUpdater: 更新不要です。"
								+ " local=" + localVersion
								+ ", remote=" + remoteVersion);
							return;
						}

						WriteLocalFileAtomically(localPath, remoteJson);

						Debug.WriteLine(
							"MapCellNameUpdater: MapCellNames.json を更新しました。"
							+ " version=" + remoteVersion);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Debug.WriteLine("MapCellNameUpdater: 通信失敗: " + ex.Message);
			}
			catch (TaskCanceledException ex)
			{
				Debug.WriteLine("MapCellNameUpdater: タイムアウト: " + ex.Message);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("MapCellNameUpdater: 更新失敗: " + ex);
			}
		}

		private static string GetLocalFilePath()
		{
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;
			var executableDirectory = Path.GetDirectoryName(executablePath)
				?? AppDomain.CurrentDomain.BaseDirectory;

			return Path.Combine(executableDirectory, "json", "MapCellNames.json");
		}

		private static string ReadLocalFile(string localPath)
		{
			if (!File.Exists(localPath))
			{
				return null;
			}

			return File.ReadAllText(localPath, Encoding.UTF8);
		}

		/// <summary>
		/// version 未指定の従来形式は 0 として扱います。
		/// </summary>
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
				Debug.WriteLine("MapCellNameUpdater: バージョン取得失敗: " + ex.Message);
				return 0;
			}
		}

		/// <summary>
		/// 現在の MapCellNameProvider が読み込める最低限の構造か検証します。
		/// </summary>
		private static bool IsValidMapCellNamesJson(string content)
		{
			try
			{
				var root = JObject.Parse(content);
				var maps = root["maps"] as JObject;

				return maps != null && maps.HasValues;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("MapCellNameUpdater: JSON検証失敗: " + ex.Message);
				return false;
			}
		}

		private static void WriteLocalFileAtomically(string localPath, string content)
		{
			var directory = Path.GetDirectoryName(localPath);
			if (string.IsNullOrEmpty(directory))
			{
				throw new InvalidOperationException("MapCellNames.json の保存先を取得できません。");
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
	}
}
