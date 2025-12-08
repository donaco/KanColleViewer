using Grabacr07.KanColleWrapper.Models.Raw;
using Nekoxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Windows; // 追加

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

		// Captured 処理を委譲するコンポーネント
		private readonly CapturedProcessor capturedProcessor;

		private KanColleClient()
		{
			this.Initialieze();

			// CapturedProcessor を初期化
			this.capturedProcessor = new CapturedProcessor(
				// getProxy
				() => this.Proxy ?? (this.Proxy = new KanColleProxy()),
				// isStartedProvider
				() => this.IsStarted,
				// onInitialized
				(start2, requireInfo) =>
				{
					try
					{
						// UI スレッドで Master/Homeport/SetRequireInfo/IsStarted を設定する
						if (Application.Current != null)
						{
							Application.Current.Dispatcher.Invoke(() =>
							{
								this.Master = new Master(start2);
								this.Homeport = new Homeport(this.Proxy);
								this.SetRequireInfo(requireInfo);
								this.IsStarted = true;
							});
						}
						else
						{
							// UI が存在しない（テスト等）の場合は通常実行
							this.Master = new Master(start2);
							this.Homeport = new Homeport(this.Proxy);
							this.SetRequireInfo(requireInfo);
							this.IsStarted = true;
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine("onInitialized handler failed: " + ex);
					}
				});

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

		// SetRequireInfo の先頭に診断ログを追加（既存メソッドを置き換え）
		private void SetRequireInfo(kcsapi_require_info data)
		{
			// Homeport の更新は UI スレッドで行う（バインディング更新を確実にするため）
			if (Application.Current != null)
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					if (data.api_basic != null)
					{
						this.Homeport.UpdateAdmiral(data.api_basic);
					}
					this.Homeport.Itemyard.Update(data.api_slot_item);
					this.Homeport.Dockyard.Update(data.api_kdock);
				});
			}
			else
			{
				if (data.api_basic != null)
				{
					this.Homeport.UpdateAdmiral(data.api_basic);
				}
				this.Homeport.Itemyard.Update(data.api_slot_item);
				this.Homeport.Dockyard.Update(data.api_kdock);
			}
		}

		/// <summary>
		/// CefSharp によって捕捉した HTTP を外部から受け取るエントリ（従来の公開 API を維持）
		/// </summary>
		public void ProcessCaptured(string url, string responseBody)
		{
			// 実処理は CapturedProcessor に委譲（初期化判定はこれで行う）
			try
			{
				this.capturedProcessor.Process(url, responseBody);
			}
			catch { /* swallow */ }

			// 追加: 起動済み/未起動に関わらず、CEF で捕まえた /kcsapi/api_port/port (および一部の重要エンドポイント)
			// を直接 Homeport に流す（プロキシを使わない環境向けのフォールバック）。
			try
			{
				if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(responseBody)) return;

				// 正規化済み JSON が既に渡される想定だが念のため正規化
				var normalized = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(responseBody);
				if (string.IsNullOrEmpty(normalized)) normalized = responseBody;

				// Helper: 実行を UI スレッドに移す
				Action<Action> runOnUi = action =>
				{
					try
					{
						if (Application.Current != null)
						{
							Application.Current.Dispatcher.BeginInvoke(new Action(() =>
							{
								try { action(); } catch { }
							}));
						}
						else
						{
							try { action(); } catch { }
						}
					}
					catch { try { action(); } catch { } }
				};

				// /kcsapi/api_port/port をパースして Homeport に反映（既存処理そのまま）
				if (url.Contains("/kcsapi/api_port/port"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_port>(normalized, out var port))
					{
						try
						{
							runOnUi(() =>
							{
								try
								{
									if (port.api_basic != null) this.Homeport.UpdateAdmiral(port.api_basic);
									if (port.api_ship != null) this.Homeport.Organization.Update(port.api_ship);
									if (port.api_ndock != null) this.Homeport.Repairyard.Update(port.api_ndock);
									if (port.api_deck_port != null) this.Homeport.Organization.Update(port.api_deck_port);

									// 連合フラグ
									this.Homeport.Organization.Combined = port.api_combined_flag != 0;

									if (port.api_material != null) this.Homeport.Materials.Update(port.api_material);
								}
								catch { }
							});
						}
						catch { }
					}

					return;
				}

				// /kcsapi/api_get_member/questlist を直接 Homeport.Quests に流す（既存）
				if (url.Contains("/kcsapi/api_get_member/questlist"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_questlist>(normalized, out var questlist))
					{
						try
						{
							runOnUi(() => this.Homeport.Quests.Update(questlist));
						}
						catch { }
					}
					return;
				}

				// 新規フォールバック: 艦娘情報 (ship, ship2)
				if (url.Contains("/kcsapi/api_get_member/ship2") || url.Contains("/kcsapi/api_get_member/ship"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship2[]>(normalized, out var ships))
					{
						try { runOnUi(() => this.Homeport.Organization.Update(ships)); } catch { }
					}
					return;
				}

				// ship3 (api_ship_data + api_deck_data)
				if (url.Contains("/kcsapi/api_get_member/ship3"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship3>(normalized, out var s3))
					{
						try
						{
							runOnUi(() =>
							{
								try
								{
									if (s3.api_ship_data != null) this.Homeport.Organization.Update(s3.api_ship_data);
									if (s3.api_deck_data != null) this.Homeport.Organization.Update(s3.api_deck_data);
								}
								catch { }
							});
						}
						catch { }
					}
					return;
				}

				// デッキ情報 (deck, deck_port)
				if (url.Contains("/kcsapi/api_get_member/deck") || url.Contains("/kcsapi/api_get_member/deck_port"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck[]>(normalized, out var decks))
					{
						try { runOnUi(() => this.Homeport.Organization.Update(decks)); } catch { }
					}
					return;
				}

				// ship_deck
				if (url.Contains("/kcsapi/api_get_member/ship_deck"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship_deck>(normalized, out var shipDeck))
					{
						try
						{
							// kcsapi_ship_deck の内部にある配列フィールドを利用して既存の Update オーバーロードを呼ぶ
							runOnUi(() =>
							{
								try
								{
									if (shipDeck.api_ship_data != null) this.Homeport.Organization.Update(shipDeck.api_ship_data);
									if (shipDeck.api_deck_data != null) this.Homeport.Organization.Update(shipDeck.api_deck_data);
								}
								catch { }
							});
						}
						catch { }
					}
					return;
				}

				// 装備一覧 (slot_item)
				if (url.Contains("/kcsapi/api_get_member/slot_item"))
				{
					if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_slotitem[]>(normalized, out var slotItems))
					{
						try { runOnUi(() => this.Homeport.Itemyard.Update(slotItems)); } catch { }
					}
					return;
				}

				// その他、将来的なフォールバック追加箇所の余地を残す（例: api_req_kousyou/*, api_req_hensei/* 等）
			}
			catch { /* swallow */ }
		}
	}
}
