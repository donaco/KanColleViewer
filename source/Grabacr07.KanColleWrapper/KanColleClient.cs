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

		// 1) フィールド追加（capturedProcessor 宣言の近くに挿入）
		private readonly HashSet<int> sortieDeckIds = new HashSet<int>();

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

			/// <summary>
			/// プロキシのイベントが発火しているかチェックするデバッグ用ログ　後で削除
			/// </summary>
			try
			{
				var proxy = this.Proxy ?? (this.Proxy = new KanColleProxy());
				proxy.ApiSessionSource
					.Subscribe(s =>
					{
						try
						{
							Debug.WriteLine("KanColleClient: ApiSessionSource fired.");
							try { Debug.WriteLine($"  Session.ToString(): {s}"); } catch { }
						}
						catch (Exception ex)
						{
							Debug.WriteLine("KanColleClient: ApiSessionSource handler failed: " + ex);
						}
					});
			}
			catch (Exception ex)
			{
				Debug.WriteLine("KanColleClient: proxy debug subscription failed: " + ex);
			}
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
		/// リファクタ: 各処理を TryHandle* 系に分割して可読性を向上
		/// </summary>
		public void ProcessCaptured(string url, string responseBody, string requestBody = null)
		{
			// 実処理は CapturedProcessor に委譲（初期化判定はこれで行う）
			try
			{
				this.capturedProcessor.Process(url, responseBody);
			}
			catch { /* swallow */ }

			try
			{
				if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(responseBody)) return;

				var normalized = Grabacr07.KanColleWrapper.Internal.Extensions.NormalizeSvDataString(responseBody);
				if (string.IsNullOrEmpty(normalized)) normalized = responseBody;

				// 先に map/start を判定して出撃フラグや該当艦隊の Sortie を行う（CEF 経路でのフォールバック）
				if (TryHandleMapStart(url, requestBody)) return;

				// 小さな責務に分割して判定する（早期 return ）
				if (TryHandlePort(url, normalized)) return;
				if (TryHandleQuestList(url, normalized)) return;
				if (TryHandleShipArray(url, normalized)) return;
				if (TryHandleShip3(url, normalized)) return;
				if (TryHandleDecks(url, normalized)) return;
				if (TryHandleShipDeck(url, normalized)) return;
				if (TryHandleSlotItems(url, normalized)) return;
				if (TryHandleBattleResult(url, normalized)) return;

				// 将来的なフォールバック追加箇所はここに追加
			}
			catch { /* swallow */ }
		}

		#region ProcessCaptured helpers (refactor)

		private void RunOnUi(Action action)
		{
			try
			{
				if (Application.Current != null && Application.Current.Dispatcher != null)
				{
					Application.Current.Dispatcher.BeginInvoke(action);
				}
				else
				{
					action();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("RunOnUi failed: " + ex);
				try { action(); } catch (Exception ex2) { Debug.WriteLine("RunOnUi fallback failed: " + ex2); }
			}
		}

		// 新規追加: map/start を処理して出撃フラグを設定するフォールバックハンドラ
		private bool TryHandleMapStart(string url, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_map/start")) return false;
			try
			{
				// requestBody は form-urlencoded の想定: "api_deck_id=1&...". null の場合は出撃フラグのみ設定。
				int deckId = -1;
				if (!string.IsNullOrEmpty(requestBody))
				{
					try
					{
						// リクエストボディが "api_deck_id=1" の形式で来ることを想定してパース
						var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
						foreach (var p in pairs)
						{
							var kv = p.Split(new[] { '=' }, 2);
							if (kv.Length == 2 && kv[0] == "api_deck_id" && int.TryParse(Uri.UnescapeDataString(kv[1]), out var id))
							{
								deckId = id;
								break;
							}
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine("TryHandleMapStart: failed to parse requestBody: " + ex);
					}
				}

				// 出撃デッキの記録（あれば）
				if (deckId > 0)
				{
					try
					{
						var org = this.Homeport?.Organization;
						if (org != null && org.Fleets.ContainsKey(deckId))
						{
							org.Fleets[deckId].Sortie();
							// 追加：出撃したデッキ ID を記録しておく
							this.sortieDeckIds.Add(deckId);
							Debug.WriteLine($"TryHandleMapStart: Fleet {deckId} marked as Sortie.");

							// 追加処理: 連合艦隊のときは第2艦隊も出撃としてマークする
							// 条件は可能な限り寛容に：組織が連合フラグを持っている、または第2艦隊に艦が存在する場合
							try
							{
								if (deckId == 1)
								{
									bool isCombined = false;
									try { isCombined = org.Combined; } catch { /* プロパティが無い場合は無視 */ }

									bool hasSecondFleet = org.Fleets.ContainsKey(2) && org.Fleets[2].Ships != null && org.Fleets[2].Ships.Length > 0;

									if (isCombined || hasSecondFleet)
									{
										if (org.Fleets.ContainsKey(2))
										{
											org.Fleets[2].Sortie();
											this.sortieDeckIds.Add(2);
											Debug.WriteLine("TryHandleMapStart: Fleet 2 also marked as Sortie (combined).");
										}
									}
								}
							}
							catch (Exception exCombined)
							{
								Debug.WriteLine("TryHandleMapStart: combined-fleet mark failed: " + exCombined);
							}
						}
						else
						{
							Debug.WriteLine($"TryHandleMapStart: Fleet {deckId} not found to mark Sortie.");
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine("TryHandleMapStart: marking fleet sortie failed: " + ex);
					}
				}

				// UI スレッドで出撃フラグを立てる（帰投処理は TryHandlePort 側で行う）
				RunOnUi(() =>
				{
					try
					{
						this.IsInSortie = true;
					}
					catch (Exception ex)
					{
						Debug.WriteLine("TryHandleMapStart.RunOnUi failed: " + ex);
					}
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleMapStart failed: " + ex);
			}

			return true;
		}

		private bool TryHandlePort(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_port/port")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_port>(normalized, out var port))
				{
					Debug.WriteLine($"TryHandlePort: deserialized port. api_ship_len={port.api_ship?.Length ?? 0}, api_deck_port_len={port.api_deck_port?.Length ?? 0}");
					RunOnUi(() =>
					{
						try
						{
							if (port.api_basic != null) this.Homeport.UpdateAdmiral(port.api_basic);
							if (port.api_ship != null) this.Homeport.Organization.Update(port.api_ship);
							if (port.api_ndock != null) this.Homeport.Repairyard.Update(port.api_ndock);
							if (port.api_deck_port != null) this.Homeport.Organization.Update(port.api_deck_port);

							this.Homeport.Organization.Combined = port.api_combined_flag != 0;

							if (port.api_material != null) this.Homeport.Materials.Update(port.api_material);

							Debug.WriteLine($"TryHandlePort: applied. Ships={this.Homeport?.Organization?.Ships?.Count}, Fleets={this.Homeport?.Organization?.Fleets?.Count}");

							// 追加: UI バインディングが更新されないケースに備え、明示的に通知を出す
							try
							{
								this.Homeport?.Organization?.NotifyUpdated();
							}
							catch (Exception exNotify)
							{
								Debug.WriteLine("TryHandlePort: NotifyUpdated failed: " + exNotify);
							}

							// --- 追加: 各艦隊を明示的に再計算・再通知して UI を確実に更新 ---
							try
							{
								var org = this.Homeport?.Organization;
								if (org != null)
								{
									foreach (var f in org.Fleets.Values)
									{
										try
										{
											// 再計算して状態を整える
											f.State.Calculate();
											f.State.Update();

											// View 側で監視されるイベントを確実に発火させる
											f.RaiseShipsUpdated();
										}
										catch (Exception exFleet)
										{
											Debug.WriteLine("TryHandlePort: fleet post-update failed: " + exFleet);
										}
									}
								}
							}
							catch (Exception exRefresh)
							{
								Debug.WriteLine("TryHandlePort: UI refresh loop failed: " + exRefresh);
							}
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandlePort.RunOnUi failed: " + ex);
						}

						// TryHandlePort 内の RunOnUi の末尾付近に追加してください
						try
						{
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								// 記録済みの出撃デッキだけを対象に Homing() を呼ぶ（誤って遠征艦隊を戻さない）
								var returning = this.sortieDeckIds.Intersect(org.Fleets.Keys).ToArray();
								foreach (var returningDeckId in returning)
								{
									try
									{
										org.Fleets[returningDeckId].Homing();
									}
									catch (Exception ex)
									{
										Debug.WriteLine("TryHandlePort: fleet Homing failed: " + ex);
									}
									// 処理済みは記録から削除
									this.sortieDeckIds.Remove(returningDeckId);
								}
							}

							// Global な出撃フラグは、まだ出撃中のデッキが残っているかで決める
							this.IsInSortie = this.sortieDeckIds.Count > 0;
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandlePort: post-processing failed: " + ex);
						}

					});
				}
				else
				{
					Debug.WriteLine("TryHandlePort: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandlePort failed: " + ex);
			}

			return true;
		}

		private bool TryHandleQuestList(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/questlist")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_questlist>(normalized, out var questlist))
				{
					RunOnUi(() => { try { this.Homeport.Quests.Update(questlist); Debug.WriteLine("TryHandleQuestList: applied."); } catch (Exception ex) { Debug.WriteLine("TryHandleQuestList.RunOnUi failed: " + ex); } });
				}
				else
				{
					Debug.WriteLine("TryHandleQuestList: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleQuestList failed: " + ex);
			}
			return true;
		}

		private bool TryHandleShipArray(string url, string normalized)
		{
			// 注意: "/kcsapi/api_get_member/ship_deck" は "/ship" を含むため誤マッチする。
			//       そのため ship_deck を明示的に除外してから処理する。
			if (!((url.Contains("/kcsapi/api_get_member/ship2") || url.Contains("/kcsapi/api_get_member/ship"))
				   && !url.Contains("/kcsapi/api_get_member/ship_deck")))
				return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship2[]>(normalized, out var ships))
				{
					Debug.WriteLine($"TryHandleShipArray: deserialized ships len={ships?.Length ?? 0}");
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Update(ships);
							Debug.WriteLine($"TryHandleShipArray: applied. Ships={this.Homeport?.Organization?.Ships?.Count}");
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandleShipArray.RunOnUi failed: " + ex);
						}
					});
				}
				else
				{
					Debug.WriteLine("TryHandleShipArray: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleShipArray failed: " + ex);
			}
			return true;
		}

		private bool TryHandleShip3(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/ship3")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship3>(normalized, out var s3))
				{
					Debug.WriteLine($"TryHandleShip3: deserialized. ship_data_len={s3.api_ship_data?.Length ?? 0}, deck_data_len={s3.api_deck_data?.Length ?? 0}");
					RunOnUi(() =>
					{
						try
						{
							if (s3.api_ship_data != null) this.Homeport.Organization.Update(s3.api_ship_data);
							if (s3.api_deck_data != null) this.Homeport.Organization.Update(s3.api_deck_data);
							Debug.WriteLine($"TryHandleShip3: applied. Ships={this.Homeport?.Organization?.Ships?.Count}, Fleets={this.Homeport?.Organization?.Fleets?.Count}");
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandleShip3.RunOnUi failed: " + ex);
						}
					});
				}
				else
				{
					Debug.WriteLine("TryHandleShip3: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleShip3 failed: " + ex);
			}
			return true;
		}

		private bool TryHandleDecks(string url, string normalized)
		{
		　// 追加エンドポイントを許可：deck / deck_port に加え、編成変更系 API も扱う
		　if (!(url.Contains("/kcsapi/api_get_member/deck")
          || url.Contains("/kcsapi/api_get_member/deck_port")
          || url.Contains("/kcsapi/api_req_hensei/change")
          || url.Contains("/kcsapi/api_req_hensei/preset_select")
          || url.Contains("/kcsapi/api_req_member/updatedeckname")))
		　return false;

			try
			{
				// まず配列として試す
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck[]>(normalized, out var decks))
				{
					Debug.WriteLine($"TryHandleDecks: deserialized decks len={decks?.Length ?? 0}");
					RunOnUi(() =>
					{
						try
						{
							// 変更: 配列を丸ごと渡すのではなく、個別要素ごとに更新する
							if (decks != null)
							{
								foreach (var deck in decks)
								{
									try
									{
										this.Homeport.Organization.Update(deck); // 単一デッキ更新を繰り返す
									}
									catch (Exception exDeck)
									{
										Debug.WriteLine("TryHandleDecks: single-deck update failed: " + exDeck);
									}
								}
							}


                            Debug.WriteLine($"TryHandleDecks: applied array (per-deck). Fleets={this.Homeport?.Organization?.Fleets?.Count}");

							// 強制的な UI 更新処理（Port ハンドラと同等の処理を行う）
							try
							{
								this.Homeport?.Organization?.NotifyUpdated();
							}
							catch (Exception exNotify)
							{
								Debug.WriteLine("TryHandleDecks: NotifyUpdated failed: " + exNotify);
							}

							try
							{
								var org = this.Homeport?.Organization;
								if (org != null)
								{
									foreach (var f in org.Fleets.Values)
									{
										try
										{
											f.State.Calculate();
											f.State.Update();
											f.RaiseShipsUpdated();
										}
										catch (Exception exFleet)
										{
											Debug.WriteLine("TryHandleDecks: fleet post-update failed: " + exFleet);
										}
									}
								}
							}
							catch (Exception exRefresh)
							{
								Debug.WriteLine("TryHandleDecks: UI refresh loop failed: " + exRefresh);
							}
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandleDecks.RunOnUi failed: " + ex);
						}
					});

					return true;
				}

				// 配列でなければ単一デッキを試す（例: 単一要素レスポンスや編成変更 API の場合）
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck>(normalized, out var singleDeck))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Update(singleDeck);

							Debug.WriteLine($"TryHandleDecks: applied single. Fleets={this.Homeport?.Organization?.Fleets?.Count}");

							try
							{
								this.Homeport?.Organization?.NotifyUpdated();
							}
							catch (Exception exNotify)
							{
								Debug.WriteLine("TryHandleDecks: NotifyUpdated (single) failed: " + exNotify);
							}

							try
							{
								var org = this.Homeport?.Organization;
								if (org != null)
								{
									foreach (var f in org.Fleets.Values)
									{
										try
										{
											f.State.Calculate();
											f.State.Update();
											f.RaiseShipsUpdated();
										}
										catch (Exception exFleet)
										{
											Debug.WriteLine("TryHandleDecks: fleet post-update (single) failed: " + exFleet);
										}
									}
								}
							}
							catch (Exception exRefresh)
							{
								Debug.WriteLine("TryHandleDecks: UI refresh loop (single) failed: " + exRefresh);
							}
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandleDecks.RunOnUi (single) failed: " + ex);
						}
					});

					return true;
				}

				Debug.WriteLine("TryHandleDecks: deserialization failed.");
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleDecks failed: " + ex);
			}

			return true;
		}

		private bool TryHandleShipDeck(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/ship_deck")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship_deck>(normalized, out var shipDeck))
				{
					Debug.WriteLine($"TryHandleShipDeck: deserialized. ship_data_len={shipDeck.api_ship_data?.Length ?? 0}, deck_data_len={shipDeck.api_deck_data?.Length ?? 0}");
					RunOnUi(() =>
					{
						try
						{
							if (shipDeck.api_ship_data != null) this.Homeport.Organization.Update(shipDeck.api_ship_data);

							// 変更: api_deck_data が部分配列 (例: 1 要素) の場合、Organization.Update(kcsapi_deck[]) に渡すと
							// Fleets コレクション全体が置き換わるため、個別要素ごとに Update(kcsapi_deck) を呼ぶようにします。
							if (shipDeck.api_deck_data != null)
							{
								foreach (var deck in shipDeck.api_deck_data)
								{
									try
									{
										this.Homeport.Organization.Update(deck); // 単一デッキ更新
									}
									catch (Exception exDeck)
									{
										Debug.WriteLine("TryHandleShipDeck: single-deck update failed: " + exDeck);
									}
								}
							}

							Debug.WriteLine($"TryHandleShipDeck: applied. Ships={this.Homeport?.Organization?.Ships?.Count}, Fleets={this.Homeport?.Organization?.Fleets?.Count}");
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandleShipDeck.RunOnUi failed: " + ex);
						}
					});
				}
				else
				{
					Debug.WriteLine("TryHandleShipDeck: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleShipDeck failed: " + ex);
			}
			return true;
		}

		private bool TryHandleSlotItems(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/slot_item")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_slotitem[]>(normalized, out var slotItems))
				{
					Debug.WriteLine($"TryHandleSlotItems: deserialized slotItems len={slotItems?.Length ?? 0}");
					RunOnUi(() => { try { this.Homeport.Itemyard.Update(slotItems); Debug.WriteLine("TryHandleSlotItems: applied."); } catch (Exception ex) { Debug.WriteLine("TryHandleSlotItems.RunOnUi failed: " + ex); } });
				}
				else
				{
					Debug.WriteLine("TryHandleSlotItems: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleSlotItems failed: " + ex);
			}
			return true;
		}

		private bool TryHandleBattleResult(string url, string normalized)
		{
			if (!(url.Contains("/kcsapi/api_req_sortie/battleresult") || url.Contains("/kcsapi/api_req_combined_battle/battleresult"))) return false;

			// 解析は試すが、主目的は UI の強制再描画
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_battleresult>(normalized, out var br))
				{
					Debug.WriteLine("TryHandleBattleResult: parsed kcsapi_battleresult.");
				}
				else if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_combined_battle_battleresult>(normalized, out var cbr))
				{
					Debug.WriteLine("TryHandleBattleResult: parsed kcsapi_combined_battle_battleresult.");
				}
				else
				{
					Debug.WriteLine("TryHandleBattleResult: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleBattleResult parse failed: " + ex);
			}

			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					if (org == null)
					{
						Debug.WriteLine("TryHandleBattleResult: Homeport.Organization is null.");
						return;
					}

					// --- 追加: 更新前の各艦隊状態を出力（診断用） ---
					Debug.WriteLine("TryHandleBattleResult: pre-update fleet states:");
					foreach (var f in org.Fleets.Values)
					{
						try
						{
							var expeditionState = f.Expedition != null ? f.Expedition.IsInExecution.ToString() : "null";
							var situation = f.State != null ? f.State.Situation.ToString() : "(null)";
							Debug.WriteLine($"  Fleet {f.Id}: IsInSortie={f.IsInSortie}, Ships={f.Ships.Length}, Expedition.IsInExecution={expeditionState}, State.Situation={situation}");
						}
						catch (Exception ex) { Debug.WriteLine("  pre-log failed: " + ex); }
					}

					// 出撃フラグに依らず全フリートを強制更新（CEF 経路では出撃検知が漏れるためのフォールバック）
					foreach (var fleet in org.Fleets.Values)
					{
						try
						{
							fleet.State.Update();
							fleet.State.Calculate();
							fleet.RaiseShipsUpdated();
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandleBattleResult: fleet update failed: " + ex);
						}
					}

					// 追加: 組織レベルでも明示通知
					try
					{
						this.Homeport?.Organization?.NotifyUpdated();
					}
					catch (Exception exNotify)
					{
						Debug.WriteLine("TryHandleBattleResult: NotifyUpdated failed: " + exNotify);
					}

					// 既にある NotifyUpdated 呼び出しの直後に以下を追加してください。
					// UI のメッセージループが落ち着いたあとに再通知することで
					// DataTemplate やバインディングの再評価を確実に促します。
					try
					{
						// UI スレッドキューの低優先度で再通知を行う
						if (Application.Current != null && Application.Current.Dispatcher != null)
						{
							Application.Current.Dispatcher.InvokeAsync(() =>
							{
								try
								{
									this.Homeport?.Organization?.NotifyUpdated();
								}
								catch (Exception exInner)
								{
									Debug.WriteLine("TryHandlePort: deferred NotifyUpdated failed: " + exInner);
								}
							}, System.Windows.Threading.DispatcherPriority.Background);
						}
					}
					catch (Exception exDeferred)
					{
						Debug.WriteLine("TryHandlePort: schedule deferred NotifyUpdated failed: " + exDeferred);
					}



					// --- 追加: 更新後の各艦隊状態を出力（診断用） ---
					Debug.WriteLine("TryHandleBattleResult: post-update fleet states:");
					foreach (var f in org.Fleets.Values)
					{
						try
						{
							var expeditionState = f.Expedition != null ? f.Expedition.IsInExecution.ToString() : "null";
							var situation = f.State != null ? f.State.Situation.ToString() : "(null)";
							Debug.WriteLine($"  Fleet {f.Id}: IsInSortie={f.IsInSortie}, Ships={f.Ships.Length}, Expedition.IsInExecution={expeditionState}, State.Situation={situation}");
						}
						catch (Exception ex) { Debug.WriteLine("  post-log failed: " + ex); }
					}

					Debug.WriteLine($"TryHandleBattleResult: forced update done. Ships={this.Homeport?.Organization?.Ships?.Count}, Fleets={this.Homeport?.Organization?.Fleets?.Count}");
				}
				catch (Exception ex)
				{
					Debug.WriteLine("TryHandleBattleResult.RunOnUi failed: " + ex);
				}
			});

			return true;
		}

		#endregion
	}
}
