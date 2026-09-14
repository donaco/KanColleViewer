using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Models
{
	internal static class TpShipSlotItemUpdater
	{
		private const int MaxResponseSizeBytes = 1 * 1024 * 1024;
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

		public static async Task UpdateLocalFileAsync()
		{
			var source = Properties.Settings.Default.TP_ShipSource;
			if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
			{
				Debug.WriteLine("TpShipSlotItemUpdater: URI が無効のため更新をスキップします。");
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
								"TpShipSlotItemUpdater: HTTP失敗 "
								+ (int)response.StatusCode
								+ " "
								+ response.ReasonPhrase);
							return;
						}

						var remoteJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
						if (!IsValidTpShipSlotItemJson(remoteJson))
						{
							Debug.WriteLine("TpShipSlotItemUpdater: サーバーJSONが不正のため更新をスキップします。");
							return;
						}

						var localPath = GetLocalFilePath();
						var localJson = ReadLocalFile(localPath);
						var remoteVersion = GetVersion(remoteJson);
						var localVersion = GetVersion(localJson);

						if (File.Exists(localPath) && remoteVersion <= localVersion)
						{
							Debug.WriteLine(
								"TpShipSlotItemUpdater: 更新不要です。"
								+ " local=" + localVersion
								+ ", remote=" + remoteVersion);
							return;
						}

						WriteLocalFileAtomically(localPath, remoteJson);

						Debug.WriteLine(
							"TpShipSlotItemUpdater: TP_Ship_SlotItem.json を更新しました。"
							+ " version=" + remoteVersion);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Debug.WriteLine("TpShipSlotItemUpdater: 通信失敗: " + ex.Message);
			}
			catch (TaskCanceledException ex)
			{
				Debug.WriteLine("TpShipSlotItemUpdater: タイムアウト: " + ex.Message);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TpShipSlotItemUpdater: 更新失敗: " + ex);
			}
		}

		private static string GetLocalFilePath()
		{
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;
			var executableDirectory = Path.GetDirectoryName(executablePath)
				?? AppDomain.CurrentDomain.BaseDirectory;

			return Path.Combine(executableDirectory, "json", "TP_Ship_SlotItem.json");
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
				Debug.WriteLine("TpShipSlotItemUpdater: バージョン取得失敗: " + ex.Message);
				return 0;
			}
		}

		private static bool IsValidTpShipSlotItemJson(string content)
		{
			try
			{
				var root = JObject.Parse(content);
				return HasValues(root["shipTypeTp"])
					|| HasValues(root["shipTp"])
					|| HasValues(root["slotItemTp"]);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TpShipSlotItemUpdater: JSON検証失敗: " + ex.Message);
				return false;
			}
		}

		private static bool HasValues(JToken token)
		{
			return token is JObject obj && obj.HasValues;
		}

		private static void WriteLocalFileAtomically(string localPath, string content)
		{
			var directory = Path.GetDirectoryName(localPath);
			if (string.IsNullOrEmpty(directory))
			{
				throw new InvalidOperationException("TP_Ship_SlotItem.json の保存先ディレクトリを取得できません。");
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
