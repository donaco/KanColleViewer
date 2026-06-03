using System.Collections.Generic;

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// KanColle API セッション（リクエスト + レスポンス）を表します。
	/// Nekoxy.Session の代替内製型です。
	/// </summary>
	public class ApiSession
	{
		public ApiRequest Request { get; }
		public ApiResponse Response { get; }

		public ApiSession(string pathAndQuery, string responseBody, IReadOnlyDictionary<string, string> requestParams = null)
		{
			this.Request = new ApiRequest(pathAndQuery, requestParams);
			this.Response = new ApiResponse(responseBody);
		}
	}

	/// <summary>
	/// API リクエスト情報。
	/// </summary>
	public class ApiRequest
	{
		public string PathAndQuery { get; }
		public IReadOnlyDictionary<string, string> Params { get; }

		public ApiRequest(string pathAndQuery, IReadOnlyDictionary<string, string> requestParams)
		{
			this.PathAndQuery = pathAndQuery;
			this.Params = requestParams ?? new Dictionary<string, string>();
		}

		/// <summary>インデクサー経由でリクエスト パラメーターへアクセスします。</summary>
		public string this[string key]
		{
			get
			{
				this.Params.TryGetValue(key, out var v);
				return v;
			}
		}
	}

	/// <summary>
	/// API レスポンス情報。
	/// </summary>
	public class ApiResponse
	{
		/// <summary>正規化済みレスポンス JSON 文字列。</summary>
		public string Body { get; }

		public ApiResponse(string body)
		{
			this.Body = body;
		}
	}
}
