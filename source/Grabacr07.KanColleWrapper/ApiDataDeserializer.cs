using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Grabacr07.KanColleWrapper
{
	public static class ApiDataDeserializer
	{
		/// <summary>
		/// レスポンスボディから svdata を抽出して、api_data 部分をデシリアライズします。
		/// 成功時に true を返し out にデシリアライズ結果をセットします。
		/// </summary>
		public static bool TryDeserializeApiData<T>(string responseBody, out T result)
		{
			result = default;
			try
			{
				var json = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(responseBody);
				// ログ: 抽出した JSON の先頭を出力（長すぎる場合は切る）
				if (!string.IsNullOrEmpty(json))
				{
					var preview = json.Length > 1000 ? json.Substring(0, 1000) + "..." : json;
					System.Diagnostics.Debug.WriteLine($"TryDeserializeApiData: extracted json preview: {preview}");
				}
				if (string.IsNullOrEmpty(json))
				{
					// 正規化失敗 → サンプルをファイルに保存して原因調査しやすくする
					try
					{
						var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs", "normalize_failed.log");
						Directory.CreateDirectory(Path.GetDirectoryName(path));
						var preview = responseBody?.Length > 2000 ? responseBody.Substring(0, 2000) + "..." : responseBody;
						File.AppendAllText(path, $"{DateTime.Now:O} url-missing-or-not-json preview:\n{preview}\n\n");
					}
					catch { }
					return false;
				}

				// Newtonsoft.Json でパースして api_data を取り出す
				JObject root;
				try
				{
					root = JObject.Parse(json);
				}
				catch (JsonException jex)
				{
					System.Diagnostics.Debug.WriteLine("TryDeserializeApiData: JObject.Parse failed: " + jex);
					// パースできない JSON を調査用ファイルに残す
					try
					{
						var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs", "parse_failed.log");
						Directory.CreateDirectory(Path.GetDirectoryName(path));
						var preview = json.Length > 4000 ? json.Substring(0, 4000) + "..." : json;
						File.AppendAllText(path, $"{DateTime.Now:O} JObject.Parse failed: {jex}\njson preview:\n{preview}\n\n");
					}
					catch { }
					return false;
				}

				var apiDataToken = root["api_data"];
				if (apiDataToken == null)
				{
					System.Diagnostics.Debug.WriteLine("TryDeserializeApiData: api_data not found.");
					try
					{
						var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs", "api_data_missing.log");
						Directory.CreateDirectory(Path.GetDirectoryName(path));
						var preview = json.Length > 4000 ? json.Substring(0, 4000) + "..." : json;
						File.AppendAllText(path, $"{DateTime.Now:O} api_data not found in json preview:\n{preview}\n\n");
					}
					catch { }
					return false;
				}

				var apiDataString = apiDataToken.ToString(Formatting.None);
				System.Diagnostics.Debug.WriteLine($"TryDeserializeApiData: api_data length = {apiDataString?.Length}");

				// 優先: DataContractJsonSerializer を使ってデシリアライズ（従来の挙動を保持）
				try
				{
					var serializer = new DataContractJsonSerializer(typeof(T));
					using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(apiDataString)))
					{
						var obj = serializer.ReadObject(ms);
						if (obj is T t) { result = t; return true; }
					}
				}
				catch (Exception exSerializer)
				{
					System.Diagnostics.Debug.WriteLine("TryDeserializeApiData: DataContractJsonSerializer failed: " + exSerializer);
					try
					{
						var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs", "deserialize_failed.log");
						Directory.CreateDirectory(Path.GetDirectoryName(path));
						var preview = apiDataString.Length > 4000 ? apiDataString.Substring(0, 4000) + "..." : apiDataString;
						File.AppendAllText(path, $"{DateTime.Now:O} DataContractJsonSerializer failed: {exSerializer}\napi_data preview:\n{preview}\n\n");
					}
					catch { }
				}

				// フォールバック: Newtonsoft.Json の ToObject<T>() を試す
				try
				{
					result = apiDataToken.ToObject<T>();
					return true;
				}
				catch (Exception exNewton)
				{
					System.Diagnostics.Debug.WriteLine("TryDeserializeApiData: Newtonsoft.Json ToObject<T> fallback failed: " + exNewton);
					try
					{
						var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs", "toobject_failed.log");
						Directory.CreateDirectory(Path.GetDirectoryName(path));
						var preview = apiDataString.Length > 4000 ? apiDataString.Substring(0, 4000) + "..." : apiDataString;
						File.AppendAllText(path, $"{DateTime.Now:O} ToObject<T> failed: {exNewton}\napi_data preview:\n{preview}\n\n");
					}
					catch { }
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("TryDeserializeApiData failed: " + ex);
			}
			return false;
		}
	}
}
