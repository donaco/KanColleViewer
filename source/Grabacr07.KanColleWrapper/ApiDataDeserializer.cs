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
				if (string.IsNullOrEmpty(json))
				{
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
					return false;
				}

				var apiDataToken = root["api_data"];
				if (apiDataToken == null)
				{
					return false;
				}

				var apiDataString = apiDataToken.ToString(Formatting.None);

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
				catch (Exception)
				{
					// DataContractJsonSerializer 失敗時は Newtonsoft にフォールバック
				}

				// フォールバック: Newtonsoft.Json の ToObject<T>() を試す
				try
				{
					result = apiDataToken.ToObject<T>();
					return true;
				}
				catch (Exception)
				{
					// フォールバックも失敗
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
