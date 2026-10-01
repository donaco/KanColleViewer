using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Grabacr07.KanColleWrapper
{
	public partial class KanColleProxy
	{
		private readonly Subject<ApiSession> _apiSessionSubject = new();
		private readonly IObservable<ApiSession> _apiSessionSource;
		private readonly IObservable<ApiSession> _apiStart2GetData;
		private readonly IObservable<ApiSession> _apiPort;
		private readonly IObservable<ApiSession> _apiGetMemberMapInfo;
		private readonly IObservable<ApiSession> _apiReqMapStart;
		private readonly IObservable<ApiSession> _apiReqMapNext;
		private readonly IObservable<ApiSession> _apiReqMapSelectEventMapRank;

		public KanColleProxy()
		{
			this._apiSessionSource = this._apiSessionSubject.AsObservable();
			this._apiStart2GetData = this._apiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_start2/getData");
			this._apiPort = this._apiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_port/port");
			this._apiGetMemberMapInfo = this._apiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_get_member/mapinfo");
			this._apiReqMapStart = this._apiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_req_map/start");
			this._apiReqMapNext = this._apiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_req_map/next");
			this._apiReqMapSelectEventMapRank = this._apiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_req_map/select_eventmap_rank");
		}

		/// <summary>
		/// KanColle API セッションを配信します。
		/// </summary>
		public IObservable<ApiSession> ApiSessionSource => this._apiSessionSource;

		/// <summary>
		/// KanColleClient から API セッションを発行します。
		/// </summary>
		internal void PublishSession(string pathAndQuery, string responseBody, IReadOnlyDictionary<string, string>? requestParams = null)
		{
			this._apiSessionSubject.OnNext(new ApiSession(pathAndQuery, responseBody, requestParams));
		}

		// ── 個別エンドポイント プロパティ (T4 生成相当) ────────────────────────────

		public IObservable<ApiSession> api_start2_getData
			=> this._apiStart2GetData;

		public IObservable<ApiSession> api_port
			=> this._apiPort;

		public IObservable<ApiSession> api_get_member_mapinfo
			=> this._apiGetMemberMapInfo;

		public IObservable<ApiSession> api_req_map_start
			=> this._apiReqMapStart;

		public IObservable<ApiSession> api_req_map_next
			=> this._apiReqMapNext;

		public IObservable<ApiSession> api_req_map_select_eventmap_rank
			=> this._apiReqMapSelectEventMapRank;
	}

	/// <summary>
	/// <see cref="IObservable{ApiSession}"/> の JSON パース拡張。
	/// </summary>
	public static class ApiSessionExtensions
	{
		/// <summary>
		/// レスポンス Body を <typeparamref name="T"/> に変換します。失敗した要素はスキップします。
		/// </summary>
		public static IObservable<SvData<T>> TryParse<T>(this IObservable<ApiSession> source) where T : class
		{
			return source
				.Select(session =>
			{
				try
				{
					var body = session.Response.Body;
					var root = JToken.Parse(body);
					var dataTok = root["api_data"] ?? root;
					var data = dataTok.ToObject<T>();
					return data == null ? null : new SvData<T>(session.Request, data);
				}
				catch
				{
					return null;
				}
			})
				.OfType<SvData<T>>();
		}
	}

	/// <summary>
	/// パース済み API レスポンスとリクエスト情報のペア。
	/// </summary>
	public class SvData<T>
	{
		public ApiRequest Request { get; }
		public T Data { get; }
		/// <summary>パースに成功した場合は常に true。失敗した要素は TryParse でフィルタ済み。</summary>
		public bool IsSuccess => true;

		public SvData(ApiRequest request, T data)
		{
			this.Request = request;
			this.Data = data;
		}
	}
}
