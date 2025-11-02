using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models.Raw;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Codeplex.Data;

namespace Grabacr07.KanColleWrapper
{
	public class KanColleClient : Notifier
	{
		#region singleton

		public static KanColleClient Current { get; } = new KanColleClient();

		#endregion

		public IKanColleClientSettings Settings { get; set; }

		/// <summary>
		/// 艦これの通信をフックするプロキシを取得します。
		/// </summary>
		public KanColleProxy Proxy { get; private set; }

		/// <summary>
		/// ユーザーに依存しないマスター情報を取得します。
		/// </summary>
		public Master Master { get; private set; }

		/// <summary>
		/// 母港の情報を取得します。
		/// </summary>
		public Homeport Homeport { get; private set; }

		#region IsStarted 変更通知プロパティ

		private bool _IsStarted;

		/// <summary>
		/// 艦これが開始されているかどうかを示す値を取得します。
		/// </summary>
		public bool IsStarted
		{
			get { return this._IsStarted; }
			set
			{
				if (this._IsStarted != value)
				{
					this._IsStarted = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsInSortie 変更通知プロパティ

		private bool _IsInSortie;

		/// <summary>
		/// 艦隊が出撃中かどうかを示す値を取得します。
		/// </summary>
		public bool IsInSortie
		{
			get { return this._IsInSortie; }
			private set
			{
				if (this._IsInSortie != value)
				{
					this._IsInSortie = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion


		private KanColleClient()
		{
			this.Initialieze();

			var start = this.Proxy.api_req_map_start;
			var end = this.Proxy.api_port;

			this.Proxy.ApiSessionSource
				.SkipUntil(start.Do(_ => this.IsInSortie = true))
				.TakeUntil(end)
				.Finally(() => this.IsInSortie = false)
				.Repeat()
				.Subscribe();
		}


		public void Initialieze()
		{
			var proxy = this.Proxy ?? (this.Proxy = new KanColleProxy());

			var start2Source = proxy.api_start2_getData.TryParse<kcsapi_start2>();
			var requireInfoSource = proxy.api_get_member_require_info.TryParse<kcsapi_require_info>();
			var firstTime = start2Source
				.CombineLatest(requireInfoSource, (start2, requireInfo) => new { start2, requireInfo, })
				.FirstAsync();

			// Homeport の初期化と require_info の適用に Master のインスタンスが必要なため、初回のみ足並み揃えて実行
			// 2 回目以降は受信したタイミングでそれぞれ更新すればよい

			firstTime.Subscribe(x =>
			{
				this.Master = new Master(x.start2.Data);
				this.Homeport = new Homeport(proxy);
				this.SetRequireInfo(x.requireInfo.Data);
				this.IsStarted = true;
			});

			start2Source
				.SkipUntil(firstTime)
				.Subscribe(x => this.Master = new Master(x.Data));

			requireInfoSource
				.SkipUntil(firstTime)
				.Subscribe(x => this.SetRequireInfo(x.Data));
		}

		private void SetRequireInfo(kcsapi_require_info data)
		{
			this.Homeport.UpdateAdmiral(data.api_basic);
			this.Homeport.Itemyard.Update(data.api_slot_item);
			this.Homeport.Dockyard.Update(data.api_kdock);
		}

		// Cef からの捕捉を扱う簡易ステート（スレッドセーフ）
		private readonly object capturedLock = new object();
		private bool capturedStart2;
		private bool capturedRequireInfo;
		private DateTime lastCapturedAt = DateTime.MinValue;

		// 追加: 実データ格納用
		private kcsapi_start2 capturedStart2Data;
		private kcsapi_require_info capturedRequireInfoData;

		/// <summary>
		/// CefSharp によって捕捉した HTTP を受け取り、初回の start2 + require_info を検出したら IsStarted を true にします。
		/// さらに、可能なら捕捉データをデシリアライズして Master / Homeport を初期化します。
		/// </summary>
		public void ProcessCaptured(string url, string responseBody)
		{
			if (string.IsNullOrEmpty(url)) return;

			try
			{
				var now = DateTime.UtcNow;

				lock (this.capturedLock)
				{
					// 念のため直近の捕捉で既に開始済みなら何もしない
					if (this.IsStarted) return;

					// /api_start2/getData を検出してデシリアライズを試みる
					if (!this.capturedStart2 && url.Contains("/api_start2/getData"))
					{
						if (TryDeserializeApiData<kcsapi_start2>(responseBody, out var start2))
						{
							this.capturedStart2 = true;
							this.capturedStart2Data = start2;
							this.lastCapturedAt = now;
							System.Diagnostics.Debug.WriteLine("ProcessCaptured: api_start2/getData deserialized.");
						}
						else
						{
							System.Diagnostics.Debug.WriteLine("ProcessCaptured: api_start2/getData detected but deserialization failed.");
						}
					}

					// /api_get_member/require_info を検出してデシリアライズを試みる
					if (!this.capturedRequireInfo && url.Contains("/api_get_member/require_info"))
					{
						if (TryDeserializeApiData<kcsapi_require_info>(responseBody, out var requireInfo))
						{
							this.capturedRequireInfo = true;
							this.capturedRequireInfoData = requireInfo;
							this.lastCapturedAt = now;
							System.Diagnostics.Debug.WriteLine("ProcessCaptured: api_get_member/require_info deserialized.");
						}
						else
						{
							System.Diagnostics.Debug.WriteLine("ProcessCaptured: api_get_member/require_info detected but deserialization failed.");
						}
					}

					// 両方デシリアライズに成功したら Master/Homeport を初期化して IsStarted = true にする
					if (this.capturedStart2 && this.capturedRequireInfo && this.capturedStart2Data != null && this.capturedRequireInfoData != null)
					{
						try
						{
							System.Diagnostics.Debug.WriteLine("ProcessCaptured: both required endpoints deserialized -> initializing Master/Homeport");

							// proxy を確保（既存の初期化ロジックに倣う）
							var proxy = this.Proxy ?? (this.Proxy = new KanColleProxy());

							this.Master = new Master(this.capturedStart2Data);
							this.Homeport = new Homeport(proxy);
							this.SetRequireInfo(this.capturedRequireInfoData);

							this.IsStarted = true;

							// リセット（必要に応じ挙動を変えてください）
							this.capturedStart2 = false;
							this.capturedRequireInfo = false;
							this.capturedStart2Data = null;
							this.capturedRequireInfoData = null;
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine("ProcessCaptured: initialization failed: " + ex);
						}
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("ProcessCaptured error: " + ex);
			}
		}

		// JSON 抽出とデシリアライズのヘルパー
		private static bool TryDeserializeApiData<T>(string responseBody, out T result)
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

				// DynamicJson でまずパースして api_data を取り出す（Quests.cs と同様の方針）
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
