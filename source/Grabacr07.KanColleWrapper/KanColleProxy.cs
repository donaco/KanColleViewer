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
		private readonly Subject<ApiSession> _apiSessionSubject = new Subject<ApiSession>();

		/// <summary>
		/// KanColle API セッションを配信します。
		/// </summary>
		public IObservable<ApiSession> ApiSessionSource => this._apiSessionSubject.AsObservable();

		/// <summary>
		/// KanColleClient から API セッションを発行します。
		/// </summary>
		internal void PublishSession(string pathAndQuery, string responseBody, IReadOnlyDictionary<string, string> requestParams = null)
		{
			this._apiSessionSubject.OnNext(new ApiSession(pathAndQuery, responseBody, requestParams));
		}

		// ── 個別エンドポイント プロパティ (T4 生成相当) ────────────────────────────

		public IObservable<ApiSession> api_start2_getData
			=> this.ApiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_start2/getData");

		public IObservable<ApiSession> api_port
			=> this.ApiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_port/port");

		public IObservable<ApiSession> api_get_member_mapinfo
			=> this.ApiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_get_member/mapinfo");

		public IObservable<ApiSession> api_req_map_start
			=> this.ApiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_req_map/start");

		public IObservable<ApiSession> api_req_map_next
			=> this.ApiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_req_map/next");

		public IObservable<ApiSession> api_req_map_select_eventmap_rank
			=> this.ApiSessionSource.Where(x => x.Request.PathAndQuery == "/kcsapi/api_req_map/select_eventmap_rank");
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
			return source.SelectMany(session =>
			{
				try
				{
					var body = session.Response.Body;
					var root = JToken.Parse(body);
					var dataTok = root["api_data"] ?? root;
					var data = dataTok.ToObject<T>();
					if (data == null) return Enumerable.Empty<SvData<T>>();
					return new[] { new SvData<T>(session.Request, data) };
				}
				catch
				{
					return Enumerable.Empty<SvData<T>>();
				}
			});
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
