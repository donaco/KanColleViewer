using System;
using System.Diagnostics;
using Grabacr07.KanColleWrapper.Models.Raw;

namespace Grabacr07.KanColleWrapper
{
	public class CapturedProcessor
	{
		private readonly object capturedLock = new object();
		private bool capturedStart2;
		private bool capturedRequireInfo;
		private DateTime lastCapturedAt = DateTime.MinValue;

		private kcsapi_start2 capturedStart2Data;
		private kcsapi_require_info capturedRequireInfoData;

		private readonly Func<KanColleProxy> getProxy;
		private readonly Func<bool> isStartedProvider;
		private readonly Action<kcsapi_start2, kcsapi_require_info> onInitialized;

		public CapturedProcessor(Func<KanColleProxy> getProxy, Func<bool> isStartedProvider, Action<kcsapi_start2, kcsapi_require_info> onInitialized)
		{
			this.getProxy = getProxy ?? throw new ArgumentNullException(nameof(getProxy));
			this.isStartedProvider = isStartedProvider ?? (() => false);
			this.onInitialized = onInitialized ?? throw new ArgumentNullException(nameof(onInitialized));
		}

		/// <summary>
		/// URL とレスポンスボディを受け取り、start2 + require_info を検出したらコールバックで初期化を行う。
		/// スレッドセーフに動作します。
		/// </summary>
		public void Process(string url, string responseBody)
		{
			if (string.IsNullOrEmpty(url)) return;

			try
			{
				var now = DateTime.UtcNow;

				lock (this.capturedLock)
				{
					// 既にアプリが開始済みなら何もしない（上位が正確ならここは早期抜け）
					if (this.isStartedProvider()) return;

					// /api_start2/getData を検出してデシリアライズを試みる
					if (!this.capturedStart2 && url.Contains("/api_start2/getData"))
					{
						if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_start2>(responseBody, out var start2))
						{
							this.capturedStart2 = true;
							this.capturedStart2Data = start2;
							this.lastCapturedAt = now;
							Debug.WriteLine("CapturedProcessor: api_start2/getData deserialized.");
						}
						else
						{
							Debug.WriteLine("CapturedProcessor: api_start2/getData detected but deserialization failed.");
						}
					}

					// /api_get_member/require_info を検出してデシリアライズを試みる
					if (!this.capturedRequireInfo && url.Contains("/api_get_member/require_info"))
					{
						if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_require_info>(responseBody, out var requireInfo))
						{
							this.capturedRequireInfo = true;
							this.capturedRequireInfoData = requireInfo;
							this.lastCapturedAt = now;
							Debug.WriteLine("CapturedProcessor: api_get_member/require_info deserialized.");
						}
						else
						{
							Debug.WriteLine("CapturedProcessor: api_get_member/require_info detected but deserialization failed.");
						}
					}

					// 両方デシリアライズに成功したらコールバックで初期化
					if (this.capturedStart2 && this.capturedRequireInfo && this.capturedStart2Data != null && this.capturedRequireInfoData != null)
					{
						try
						{
							Debug.WriteLine("CapturedProcessor: both required endpoints deserialized -> invoking onInitialized");

							// Ensure proxy exists in same manner as previous logic
							var proxy = this.getProxy();

							// コールバック実行（KanColleClient 側で Master/Homeport を構築）
							this.onInitialized(this.capturedStart2Data, this.capturedRequireInfoData);

							// 初期化後はフラグとデータをクリア
							this.capturedStart2 = false;
							this.capturedRequireInfo = false;
							this.capturedStart2Data = null;
							this.capturedRequireInfoData = null;
						}
						catch (Exception ex)
						{
							Debug.WriteLine("CapturedProcessor: initialization callback failed: " + ex);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("CapturedProcessor error: " + ex);
			}
		}
	}
}
