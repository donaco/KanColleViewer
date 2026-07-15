using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace Grabacr07.KanColleViewer.Models
{
	public class SallyArea
	{
		private const int MaxResponseSizeBytes = 1 * 1024 * 1024;
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

		public int Area { get; private set; }

		public string Name { get; private set; }

		public Color Color { get; private set; } = Colors.Transparent;

		private SallyArea() { }

		public static SallyArea Default { get; } = new SallyArea();

		/// <summary>
		/// 起動時にサーバー上の EventMap.json を確認し、ローカルより新しい場合だけ更新します。
		/// このメソッド内で発生した例外はすべて処理し、起動処理へ伝播させません。
		/// </summary>
		public static async Task UpdateLocalFileAsync()
		{
			var source = Properties.Settings.Default.SallyAreaSource;
			if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
			{
				Debug.WriteLine("SallyArea.UpdateLocalFileAsync: URI が無効のため更新をスキップします。");
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
								"SallyArea.UpdateLocalFileAsync: HTTP失敗 "
								+ (int)response.StatusCode + " " + response.ReasonPhrase);
							return;
						}

						var remoteJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
						var remoteAreas = ParseAreas(remoteJson);
						if (remoteAreas.Length == 0)
						{
							Debug.WriteLine("SallyArea.UpdateLocalFileAsync: サーバーJSONが無効または空のため更新をスキップします。");
							return;
						}

						var localPath = GetLocalFilePath();
						var localJson = ReadLocalFile(localPath);
						var remoteVersion = GetVersion(remoteJson);
						var localVersion = GetVersion(localJson);

						if (File.Exists(localPath) && remoteVersion <= localVersion)
						{
							Debug.WriteLine(
								"SallyArea.UpdateLocalFileAsync: 更新不要です。"
								+ " local=" + localVersion
								+ ", remote=" + remoteVersion);
							return;
						}

						WriteLocalFileAtomically(localPath, remoteJson);
						Debug.WriteLine(
							"SallyArea.UpdateLocalFileAsync: EventMap.json を更新しました。"
							+ " version=" + remoteVersion
							+ ", 件数=" + remoteAreas.Length);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Debug.WriteLine("SallyArea.UpdateLocalFileAsync: 通信失敗: " + ex.Message);
			}
			catch (TaskCanceledException ex)
			{
				Debug.WriteLine("SallyArea.UpdateLocalFileAsync: タイムアウト: " + ex.Message);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("SallyArea.UpdateLocalFileAsync: 更新失敗: " + ex);
			}
		}

		/// <summary>
		/// ゲーム中はサーバー通信を行わず、ローカルの EventMap.json のみを読み込みます。
		/// 既存の呼び出し元との互換性のため Task を返します。
		/// </summary>
		public static Task<SallyArea[]> GetAsync()
		{
			return Task.FromResult(LoadFromLocalFile());
		}

		private static string GetLocalFilePath()
		{
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;
			var executableDirectory = Path.GetDirectoryName(executablePath)
				?? AppDomain.CurrentDomain.BaseDirectory;

			return Path.Combine(executableDirectory, "json", "EventMap.json");
		}

		private static SallyArea[] LoadFromLocalFile()
		{
			try
			{
				var localPath = GetLocalFilePath();
				var json = ReadLocalFile(localPath);
				var parsed = ParseAreas(json);

				Debug.WriteLine(
					"SallyArea.LoadFromLocalFile: ローカル読込件数="
					+ parsed.Length);

				return parsed;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("SallyArea.LoadFromLocalFile: 読込失敗: " + ex);
				return Array.Empty<SallyArea>();
			}
		}

		private static string ReadLocalFile(string localPath)
		{
			if (!File.Exists(localPath))
			{
				Debug.WriteLine("SallyArea.ReadLocalFile: ファイルなし: " + localPath);
				return null;
			}

			return File.ReadAllText(localPath, Encoding.UTF8);
		}

		private static void WriteLocalFileAtomically(string localPath, string content)
		{
			var directory = Path.GetDirectoryName(localPath);
			if (string.IsNullOrEmpty(directory))
			{
				throw new InvalidOperationException("EventMap.json の保存先ディレクトリを取得できません。");
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

		/// <summary>
		/// 新形式の { "version": 1, "EventMap": [...] } からバージョンを読み込みます。
		/// 従来の配列形式は version 0 として扱います。
		/// </summary>
		private static long GetVersion(string content)
		{
			try
			{
				var trimmed = (content ?? string.Empty).TrimStart();
				if (!trimmed.StartsWith("{"))
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
				Debug.WriteLine("SallyArea.GetVersion: バージョン取得失敗: " + ex.Message);
				return 0;
			}
		}

		private static SallyArea[] ParseAreas(string content)
		{
			try
			{
				var trimmed = (content ?? string.Empty).TrimStart();
				JToken listToken = null;

				if (trimmed.StartsWith("["))
				{
					// 従来形式: [ { "area": "...", ... } ]
					listToken = JArray.Parse(content);
				}
				else if (trimmed.StartsWith("{"))
				{
					// 新形式: { "version": 1, "EventMap": [ ... ] }
					var root = JObject.Parse(content);
					listToken = root["EventMap"];
				}

				var array = listToken as JArray;
				if (array == null)
				{
					return Array.Empty<SallyArea>();
				}

				return array
					.OfType<JObject>()
					.Select(x => new SallyArea
					{
						Area = ParseArea(x["area"]),
						Name = (string)x["name"] ?? string.Empty,
						Color = Helper.StringToColor((string)x["color"] ?? string.Empty),
					})
					.ToArray();
			}
			catch (Exception ex)
			{
				Debug.WriteLine("SallyArea.ParseAreas: 解析失敗: " + ex);
				return Array.Empty<SallyArea>();
			}
		}

		private static int ParseArea(JToken token)
		{
			var value = token?.Value<string>();
			if (!string.IsNullOrWhiteSpace(value))
			{
				var separatorIndex = value.IndexOf('-');
				if (separatorIndex > 0)
				{
					value = value.Substring(0, separatorIndex);
				}

				if (int.TryParse(value, out var area))
				{
					return area;
				}
			}

			return token?.Value<int?>() ?? 0;
		}
	}
}
