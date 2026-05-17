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

		private readonly Func<bool> isStartedProvider;
		private readonly Action<kcsapi_start2, kcsapi_require_info> onInitialized;

		public CapturedProcessor(Func<bool> isStartedProvider, Action<kcsapi_start2, kcsapi_require_info> onInitialized)
		{
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

			// lock 外で呼び出すコールバック引数を保持する変数
			// null のままであれば lock 内で条件が揃わなかったことを示す
			kcsapi_start2 pendingStart2 = null;
			kcsapi_require_info pendingRequireInfo = null;

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

					// 両方揃ったらコールバック引数をローカルに退避してフラグをクリアする。
					// onInitialized 自体は lock 解放後に呼び出す。
					// これにより onInitialized 内の Dispatcher.Invoke（同期）がデッドロックを
					// 引き起こすリスクを排除する。
					if (this.capturedStart2 && this.capturedRequireInfo
						&& this.capturedStart2Data != null && this.capturedRequireInfoData != null)
					{
						pendingStart2 = this.capturedStart2Data;
						pendingRequireInfo = this.capturedRequireInfoData;
						this.capturedStart2 = false;
						this.capturedRequireInfo = false;
						this.capturedStart2Data = null;
						this.capturedRequireInfoData = null;
					}
				}

				// lock の外で onInitialized を呼び出す
				if (pendingStart2 != null && pendingRequireInfo != null)
				{
					try
					{
						Debug.WriteLine("CapturedProcessor: both required endpoints deserialized -> invoking onInitialized");
						this.onInitialized(pendingStart2, pendingRequireInfo);
					}
					catch (Exception ex)
					{
						Debug.WriteLine("CapturedProcessor: initialization callback failed: " + ex);
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
