using Grabacr07.KanColleWrapper.Models;
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
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows; // 追加
using System.Xml.Linq;

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
			// 確認用ログ（後で削除）
			try
			{
				Debug.WriteLine($"ProcessCaptured ENTER: url={url}, requestBody_len={(requestBody == null ? 0 : requestBody.Length)}, responseBody_len={(responseBody == null ? 0 : responseBody.Length)}");
			}
			catch { }

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

				// 確認用ログ（レスポンスのプレビューを出す）
				try
				{
					var preview = normalized.Length > 300 ? normalized.Substring(0, 300) + "..." : normalized;
					Debug.WriteLine($"ProcessCaptured: normalized preview length={normalized.Length}, preview={preview}");
				}
				catch { }

				// 先に map/start を判定して出撃フラグや該当艦隊の Sortie を行う（CEF 経路でのフォールバック）
				if (TryHandleMapStart(url, requestBody)) return;

				// 小さな責務に分割して判定する（早期 return ）
				if (TryHandlePort(url, normalized)) return;
				if (TryHandleQuestList(url, normalized)) return;
				if (TryHandleShipArray(url, normalized)) return;
				if (TryHandleSlotExchangeIndex(url, normalized, requestBody)) return;
				if (TryHandleShip3(url, normalized)) return;
				if (TryHandleCharge(url, normalized)) return;
				if (TryHandleDecks(url, normalized)) return;
				if (TryHandleShipDeck(url, normalized)) return;
				if (TryHandleSlotItems(url, normalized)) return;
				if (TryHandleBattleResult(url, normalized)) return;
				if (TryHandleNyukyoSpeedChange(url, requestBody)) return;
				if (TryHandleNyukyoStart(url, requestBody)) return;
				if (TryHandleNdockList(url, normalized)) return;
				if (TryHandlePort(url, normalized)) return;

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

		/// <summary>
		/// 出撃フラグ
		/// </summary>
		private bool TryHandleMapStart(string url, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_map/start")) return false;
			try
			{
				int deckId = -1;
				if (!string.IsNullOrEmpty(requestBody))
				{
					try
					{
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
					catch
					{
					}
				}

				if (deckId > 0)
				{
					try
					{
						var org = this.Homeport?.Organization;
						if (org != null && org.Fleets.ContainsKey(deckId))
						{
							org.Fleets[deckId].Sortie();
							// 記録：出撃したデッキ ID を保存
							this.sortieDeckIds.Add(deckId);

							// 第1艦隊が出撃かつ組合せフラグが立っている場合のみ第2艦隊も出撃扱いにする
							if (deckId == 1)
							{
								bool isCombined = false;
								try { isCombined = org.Combined; } catch { isCombined = false; }

								if (isCombined && org.Fleets.ContainsKey(2))
								{
									org.Fleets[2].Sortie();
									this.sortieDeckIds.Add(2);
								}
							}
						}
					}
					catch
					{
					}
				}

				RunOnUi(() =>
				{
					try
					{
						this.IsInSortie = true;
					}
					catch
					{
					}
				});
			}
			catch
			{
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

							// 追加: UI バインディングが更新されないケースに備え、明示的に通知を出す
							try
							{
								this.Homeport?.Organization?.NotifyUpdated();
							}
							catch
							{
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
										catch
										{
										}
									}
								}
							}
							catch
							{
							}
						}
						catch
						{
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
									catch
									{
									}
									// 処理済みは記録から削除
									this.sortieDeckIds.Remove(returningDeckId);
								}
							}

							// Global な出撃フラグは、まだ出撃中のデッキが残っているかで決める
							this.IsInSortie = this.sortieDeckIds.Count > 0;
						}
						catch
						{
						}

					});
				}
				else
				{
				}
			}
			catch
			{
			}

			return true;
		}
		/// <summary>
		/// 任務一覧
		/// </summary>
		private bool TryHandleQuestList(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/questlist")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_questlist>(normalized, out var questlist))
				{
					RunOnUi(() => { 
						try { this.Homeport.Quests.Update(questlist);
						} 
						catch
						{ 
						} });
				}
				else
				{
				}
			}
			catch
			{
			}
			return true;
		}

		private bool TryHandleShipArray(string url, string normalized)
		{
			// 注意: "/kcsapi/api_get_member/ship_deck" は "/ship" を含むため誤マッチする。
			//       "/ship3" も "/ship" を含むため誤マッチするので除外する。
			if (!((url.Contains("/kcsapi/api_get_member/ship2") || url.Contains("/kcsapi/api_get_member/ship"))
				   && !url.Contains("/kcsapi/api_get_member/ship_deck")
				   && !url.Contains("/kcsapi/api_get_member/ship3")))
				return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship2[]>(normalized, out var ships))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Update(ships);
						}
						catch
						{
						}
					});
				}
				else
				{
				}
			}
			catch
			{
			}
			return true;
		}
		/// <summary>
		/// 改装系1
		/// </summary>
		private bool TryHandleShip3(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/ship3")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ship3>(normalized, out var s3))
				{
					RunOnUi(() =>
					{
						try
						{
							var org = this.Homeport?.Organization;
							var updatedShipIds = new List<int>();

							// ship データを個別に確実に反映
							if (s3.api_ship_data != null)
							{
								foreach (var rawShip in s3.api_ship_data)
								{
									try
									{
										// 既存 Ship インスタンスがあれば直接更新
										var existing = org?.Ships?[rawShip.api_id];
										if (existing != null)
										{
											existing.Update(rawShip);
										}
										else
										{
											// もし存在しなければ既存の更新ルートにフォールバック
											try { this.Homeport.Organization.Update(new[] { rawShip }); } catch { }
										}

										updatedShipIds.Add(rawShip.api_id);
									}
									catch { /* 個別失敗は無視して続行 */ }
								}
							}

							// デッキ情報は個別デッキごとに更新
							if (s3.api_deck_data != null)
							{
								foreach (var deck in s3.api_deck_data)
								{
									try { this.Homeport.Organization.Update(deck); } catch { }
								}
							}

							// 更新された艦を含む艦隊のみ再計算・再通知
							if (org != null && updatedShipIds.Count > 0)
							{
								var affectedFleets = org.Fleets.Values
									.Where(f => f.Ships.Any(s => updatedShipIds.Contains(s.Id)))
									.ToArray();

								foreach (var f in affectedFleets)
								{
									try { f.State.Update(); } catch { }
									try { f.State.Calculate(); } catch { }
									try { f.RaiseShipsUpdated(); } catch { }
								}
							}

							// 組織レベルで再通知して DataTemplate 等の再評価を促す
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
						}
						catch { /* swallow */ }
					});
				}
			}
			catch { /* swallow */ }

			return true;
		}

		/// <summary>
		/// 改装系2 -装備スロット交換
		/// </summary>
		private bool TryHandleSlotExchangeIndex(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/slot_exchange_index")) return false;

			// requestBody から ship id を先に探す（api_id, api_ship_id の両方を確認）
			int shipId = -1;
			if (!string.IsNullOrEmpty(requestBody))
			{
				var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var p in pairs)
				{
					var kv = p.Split(new[] { '=' }, 2);
					if (kv.Length != 2) continue;
					var key = kv[0];
					var val = Uri.UnescapeDataString(kv[1]);
					if (key == "api_id" || key == "api_ship_id")
					{
						int.TryParse(val, out shipId);
						break;
					}
				}
			}

			// --- JSON 側を解析して api_slot を柔軟に抽出 ---
			JToken root;
			try
			{
				root = JToken.Parse(normalized);
			}
			catch
			{
				// JSON 解析できなければ終了
				return true;
			}

			JToken data = root["api_data"] ?? root;

			JToken slotToken = null;

			// 1) api_ship_data がある場合はそちらから探す（単一オブジェクト or 配列）
			var shipData = data["api_ship_data"];
			if (shipData != null)
			{
				if (shipData.Type == JTokenType.Object)
				{
					// 単一オブジェクト
					slotToken = shipData["api_slot"];
				}
				else if (shipData.Type == JTokenType.Array)
				{
					// 配列の場合は shipId に一致する要素を探す。なければ最初の要素にフォールバック。
					if (shipId > 0)
					{
						foreach (var elem in shipData.Children())
						{
							var idToken = elem["api_id"] ?? elem["api_ship_id"];
							if (idToken != null && idToken.Type == JTokenType.Integer && idToken.Value<int>() == shipId)
							{
								slotToken = elem["api_slot"];
								break;
							}
						}
					}
					if (slotToken == null)
					{
						// フォールバック: 最初の要素
						var first = shipData.First;
						if (first != null) slotToken = first["api_slot"];
					}
				}
			}

			// 2) 見つからなければ data.api_slot を探す
			if (slotToken == null)
			{
				slotToken = data["api_slot"] ?? data.SelectToken("api_slot");
			}

			if (slotToken == null) return true;

			// slotToken が配列であることを確認
			JArray apiSlotArray = slotToken as JArray;
			if (apiSlotArray == null)
			{
				// 場合によっては api_slot がオブジェクト内にある別形式の可能性もあるため失敗は無視
				return true;
			}

			var apiSlot = apiSlotArray.Select(t => (int?)t ?? 0).ToArray();

			// shipId がまだ不明なら、もし shipData が単一オブジェクトならそこから取得
			if (shipId <= 0 && shipData != null && shipData.Type == JTokenType.Object)
			{
				var idTok = shipData["api_id"] ?? shipData["api_ship_id"];
				if (idTok != null && idTok.Type == JTokenType.Integer) shipId = idTok.Value<int>();
			}

			if (shipId <= 0) return true;

			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					var ship = org?.Ships?[shipId];
					if (ship == null) return;

					// RawData.api_slot を置き換えて UpdateSlots() を呼ぶ（Organization.ExchangeSlot と同等）
					ship.RawData.api_slot = apiSlot;
					ship.UpdateSlots();

					// 所属艦隊を再計算・再通知
					var fleet = org.GetFleet(ship.Id);
					if (fleet != null)
					{
						try { fleet.State.Calculate(); } catch { }
						try { fleet.State.Update(); } catch { }
						try { fleet.RaiseShipsUpdated(); } catch { }
					}

					// 組織レベルの再通知で UI 再評価を促す
					try { org.NotifyUpdated(); } catch { }
				}
				catch
				{
				}
			});

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
									catch
									{
									}
								}
							}

							// 強制的な UI 更新処理（Port ハンドラと同等の処理を行う）
							try
							{
								this.Homeport?.Organization?.NotifyUpdated();
							}
							catch
							{
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
										catch
										{
										}
									}
								}
							}
							catch
							{
							}
						}
						catch
						{
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
					RunOnUi(() =>
					{
						try
						{
							// 明示的に kcsapi_ship_deck 型にキャストして Update を呼ぶ（オーバーロードの誤選択を防ぐ）
							this.Homeport.Organization.Update(shipDeck);

							// UI の再評価を確実に促す
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }

							// フリート状態の再計算・再通知
							try
							{
								var org = this.Homeport?.Organization;
								if (org != null)
								{
									foreach (var f in org.Fleets.Values)
									{
										try { f.State.Calculate(); } catch { }
										try { f.State.Update(); } catch { }
										try { f.RaiseShipsUpdated(); } catch { }
									}
								}
							}
							catch { }
						}
						catch (Exception ex)
						{
						}
					});
				}
				else
				{
				}
			}
			catch
			{
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
				}
				else if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_combined_battle_battleresult>(normalized, out var cbr))
				{
				}
				else
				{
				}
			}
			catch
			{
			}

			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					if (org == null)
					{

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
		/// <summary>
		/// 入渠系1 ドック一覧
		/// </summary>
		private bool TryHandleNdockList(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/ndock")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ndock[]>(normalized, out var ndocks))
				{
					Debug.WriteLine($"TryHandleNdockList: deserialized ndocks len={ndocks?.Length ?? 0}");
					RunOnUi(() =>
					{
						try
						{
							this.Homeport?.Repairyard?.Update(ndocks);
							Debug.WriteLine("TryHandleNdockList: applied.");
						}
						catch (Exception ex)
						{
							Debug.WriteLine("TryHandleNdockList.RunOnUi failed: " + ex);
						}
					});
				}
				else
				{
					Debug.WriteLine("TryHandleNdockList: deserialization failed.");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleNdockList failed: " + ex);
			}

			return true;
		}

		/// <summary>
		/// 入渠系2 入渠開始
		/// </summary>
		private bool TryHandleNyukyoStart(string url, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_nyukyo/start")) return false;

			try
			{
				// requestBody をパースして api_ship_id/api_highspeed を取得する（form-urlencoded 想定）
				if (string.IsNullOrEmpty(requestBody)) return true;

				var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var pair in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
				{
					var kv = pair.Split(new[] { '=' }, 2);
					if (kv.Length == 2)
					{
						try { dict[kv[0]] = Uri.UnescapeDataString(kv[1]); } catch { dict[kv[0]] = kv[1]; }
					}
				}

				if (!dict.ContainsKey("api_ship_id")) return true;

				int shipId;
				if (!int.TryParse(dict["api_ship_id"], out shipId)) return true;

				var highspeed = dict.ContainsKey("api_highspeed") && dict["api_highspeed"] == "1";

				RunOnUi(() =>
				{
					try
					{
						var ship = this.Homeport?.Organization?.Ships?[shipId];
						if (ship == null) return;

						// 既存の Repairyard.Start と同様、高速修復材使用なら即時 Repair を反映
						if (highspeed)
						{
							ship.Repair();
							this.Homeport?.Organization?.GetFleet(ship.Id)?.State.Update();
						}
						// 通常入渠開始はドック一覧 (ndock) が後で来る想定なのでここでは無理に触らない
						Debug.WriteLine($"TryHandleNyukyoStart: processed shipId={shipId}, highspeed={highspeed}");
					}
					catch (Exception ex)
					{
						Debug.WriteLine("TryHandleNyukyoStart.RunOnUi failed: " + ex);
					}
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine("TryHandleNyukyoStart failed: " + ex);
			}

			return true;
		}
		
		/// <summary>
		/// 入渠系3 高速修復材
		/// </summary>
		private bool TryHandleNyukyoSpeedChange(string url, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_nyukyo/speedchange")) return false;

			try
			{
				// requestBody をパースして api_ndock_id を取得する（form-urlencoded 想定）
				if (string.IsNullOrEmpty(requestBody)) return true;

				var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var pair in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
				{
					var kv = pair.Split(new[] { '=' }, 2);
					if (kv.Length == 2)
					{
						try { dict[kv[0]] = Uri.UnescapeDataString(kv[1]); } catch { dict[kv[0]] = kv[1]; }
					}
				}

				if (!dict.ContainsKey("api_ndock_id")) return true;
				if (!int.TryParse(dict["api_ndock_id"], out var ndockId)) return true;

				RunOnUi(() =>
				{
					try
					{
						var dock = this.Homeport?.Repairyard?.Docks?[ndockId];
						var ship = dock?.Ship;
						if (dock != null) dock.Finish();
						if (ship != null)
						{
							ship.Repair();
							this.Homeport?.Organization?.GetFleet(ship.Id)?.State.Update();
						}
					}
					catch
					{
					}
				});
			}
			catch
			{
			}

			return true;
		}

		/// <summary>
		/// 補給処理
		/// </summary>
		private bool TryHandleCharge(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_hokyu/charge")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_charge>(normalized, out var charge))
				{
					// charge.api_material : int[] (length=4) — Materials の private Update(int[]) を反射で呼び出して反映
					// charge.api_ship : kcsapi_charge_ship[] — 各艦の燃料/弾薬/onslot を更新し艦隊状態を再計算
					RunOnUi(() =>
					{
						try
						{
							// Materials の private Update(int[]) をリフレクションで呼ぶ
							var materials = this.Homeport?.Materials;
							if (materials != null && charge.api_material != null)
							{
								var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
								mi?.Invoke(materials, new object[] { charge.api_material });
							}

							// Ships の補給反映
							if (charge.api_ship != null && charge.api_ship.Length > 0)
							{
								Fleet affectedFleet = null;
								var org = this.Homeport?.Organization;
								foreach (var s in charge.api_ship)
								{
									try
									{
										var ship = org?.Ships?[s.api_id];
										if (ship == null) continue;

										ship.Charge(s.api_fuel, s.api_bull, s.api_onslot);

										if (affectedFleet == null) affectedFleet = org.GetFleet(ship.Id);
									}
									catch
									{
									}
								}

								if (affectedFleet != null)
								{
									try { affectedFleet.State.Update(); } catch { }
									try { affectedFleet.State.Calculate(); } catch { }
								}
							}

							// 全体の UI 再評価を促す
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
						}
						catch
						{
						}
					});
				}
			}
			catch
			{
			}

			return true;
		}
	}
}
