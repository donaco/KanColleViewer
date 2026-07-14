using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Grabacr07.KanColleWrapper
{
	public static class ApiDataDeserializer
	{
		private static readonly JsonSerializer _tolerantSerializer;

		static ApiDataDeserializer()
		{
			_tolerantSerializer = new JsonSerializer
			{
				NullValueHandling = NullValueHandling.Ignore,
				MissingMemberHandling = MissingMemberHandling.Ignore,
			};
			_tolerantSerializer.Error += (sender, args) => { args.ErrorContext.Handled = true; };
		}

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
					return false;

				JObject root;
				try
				{
					root = JObject.Parse(json);
				}
				catch (JsonException jex)
				{
					System.Diagnostics.Debug.WriteLine("TryDeserializeApiData: JObject.Parse failed: " + jex.Message);
					return false;
				}

				var apiDataToken = root["api_data"];
				if (apiDataToken == null || apiDataToken.Type == JTokenType.Null)
					return false;

				// _tolerantSerializer を使用して内部 ArgumentNullException を抑制
				result = apiDataToken.ToObject<T>(_tolerantSerializer);
				return result != null;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("TryDeserializeApiData failed: " + ex.Message);
			}
			return false;
		}
	}
}
