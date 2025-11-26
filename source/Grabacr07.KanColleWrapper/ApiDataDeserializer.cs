using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Codeplex.Data;

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
				var json = ExtractSvDataJson(responseBody);
				// ログ: 抽出した JSON の先頭を出力（長すぎる場合は切る）
				if (!string.IsNullOrEmpty(json))
				{
					var preview = json.Length > 1000 ? json.Substring(0, 1000) + "..." : json;
					System.Diagnostics.Debug.WriteLine($"TryDeserializeApiData: extracted json preview: {preview}");
				}
				if (string.IsNullOrEmpty(json)) return false;

				// DynamicJson でまずパースして api_data を取り出す（従来の方針を踏襲）
				dynamic djson = DynamicJson.Parse(json);
				var apiData = djson.api_data;
				if (apiData == null)
				{
					System.Diagnostics.Debug.WriteLine("TryDeserializeApiData: api_data not found.");
					return false;
				}

				var apiDataString = apiData.ToString();
				System.Diagnostics.Debug.WriteLine($"TryDeserializeApiData: api_data length = {apiDataString?.Length}");

				// 優先: DataContractJsonSerializer を使ってデシリアライズ
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
				}

				// フォールバック: DynamicJson の Deserialize<T>() を試す
				try
				{
					// apiData が既に DynamicJson の場合
					if (apiData is DynamicJson dyn)
					{
						result = dyn.Deserialize<T>();
						return true;
					}

					// 文字列として再パースしてから Deserialize を試す
					var dyn2 = DynamicJson.Parse(apiDataString);
					result = dyn2.Deserialize<T>();
					return true;
				}
				catch (Exception exDyn)
				{
					System.Diagnostics.Debug.WriteLine("TryDeserializeApiData: DynamicJson.Deserialize fallback failed: " + exDyn);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("TryDeserializeApiData failed: " + ex);
			}
			return false;
		}

		// svdata= prefix や不要プレフィックスを取り除いて JSON 本体を返す（null 可）
		private static string ExtractSvDataJson(string s)
		{
			if (string.IsNullOrEmpty(s)) return null;

			// svdata= prefix がある場合はその後を使う
			var idx = s.IndexOf("svdata=");
			if (idx >= 0)
			{
				s = s.Substring(idx + "svdata=".Length);
			}

			// 一部レスポンスは "throw 1; < don't be evil' >{...}" のようなプレフィックスがあるため最初の '{' から切り出す
			var firstBrace = s.IndexOf('{');
			if (firstBrace >= 0)
			{
				s = s.Substring(firstBrace);
			}

			return s.Trim();
		}
	}
}
