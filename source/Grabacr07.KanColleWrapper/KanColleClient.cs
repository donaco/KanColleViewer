using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Nekoxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
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
		private Homeport _Homeport;
		public Homeport Homeport
		{
			get { return this._Homeport; }
			private set
			{
				if (this._Homeport != value)
				{
					this._Homeport = value;
					this.RaisePropertyChanged();
				}
			}
		}

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

		#region SortieInfo プロパティ

		/// <summary>
		/// 出撃中のマップ位置情報を取得します。
		/// </summary>
		public SortieInfo SortieInfo { get; } = new SortieInfo();

		#endregion

		// Captured 処理を委譲するコンポーネント
		private readonly CapturedProcessor capturedProcessor;

		// 出撃中の艦隊ID記録
		private readonly HashSet<int> sortieDeckIds = new HashSet<int>();

		// 建造でキャッシュする消費資源
		private readonly Dictionary<int, int[]> pendingCreateMaterials = new Dictionary<int, int[]>();

		// 建造
		private readonly HashSet<int> appliedBuildKdock = new HashSet<int>();

		// 入渠
		private readonly HashSet<int> appliedRepairNdock = new HashSet<int>();

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
					catch
					{
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
						}
						catch (Exception)
						{
						}
					});
			}
			catch (Exception)
			{
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

		// 直近に処理した api_req_hensei/change の deckId を一時保持する（requestBody が来ない時用）
		private int lastChangeDeckId = -1;

		// 直近に処理した建造ドック ID を保持（createship の requestBody が届く時用）
		private int lastCreateKdockId = -1;

		// start/next で取得した cellNo をキャッシュ（battle で使用）
		private int cachedCellNo = 0;

		/// <summary>
		/// CefSharp によって捕捉した HTTP を外部から受け取るエントリ（従来の公開 API を維持）
		/// リファクタ: 各処理を TryHandle* 系に分割して可読性を向上
		/// </summary>
		public void ProcessCaptured(string url, string responseBody, string requestBody = null)
		{
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

				// （ProcessCaptured 内のハンドラ呼び出し群を以下に置換）
				// 先に map/start を判定して出撃フラグや該当艦隊の Sortie を行う（CEF 経路でのフォールバック）
				if (TryHandleMapStart(url, requestBody, normalized)) return;
				if (TryHandleBattle(url, normalized)) return;
				if (TryHandleMapNext(url, normalized)) return;
				if (TryHandleMapInfo(url, normalized)) return;

				// 小さな処理に分割して判定（早期 return ）
				if (TryHandlePort(url, normalized)) return;

				// 任務完了や個別素材/消費アイテムの更新
				if (TryHandleClearItemGet(url, normalized)) return;
				if (TryHandleDestroyItem2(url, normalized, requestBody)) return;
				if (TryHandleDestroyShip(url, normalized, requestBody)) return;
				if (TryHandlePowerup(url, normalized, requestBody)) return;
				if (TryHandleMaterial(url, normalized)) return;
				if (TryHandleUseItem(url, normalized)) return;

				if (TryHandleQuestList(url, normalized)) return;
				if (TryHandleShipArray(url, normalized)) return;

				// 装備系
				if (TryHandleSlotExchangeIndex(url, normalized, requestBody)) return;
				if (TryHandleSlotDeprive(url, normalized, requestBody)) return;
				if (TryHandleOpenExslot(url, normalized, requestBody)) return;
				if (TryHandleSlotsetEx(url, normalized, requestBody)) return;
				if (TryHandleShip3(url, normalized)) return;

				if (TryHandleCharge(url, normalized)) return;

				// preset_select は専用処理（※TryHandleDecks より前に配置）
				if (TryHandlePresetSelect(url, normalized)) return;

				// 艦隊名更新 は requestBody を使用して即時反映
				if (TryHandleUpdatedeckname(url, normalized, requestBody)) return;

				// 編成情報一般（deck / deck_port / change / preset_select など）
				if (TryHandleDecks(url, normalized, requestBody)) return;
				if (TryHandleShipDeck(url, normalized)) return;
				if (TryHandlePresetDeck(url, normalized)) return;
				if (TryHandleHenseiCombined(url, normalized)) return;
				if (TryHandleSlotItems(url, normalized)) return;
				if (TryHandleCreateItem(url, normalized, requestBody)) return;

				// 建造系
				if (TryHandleCreateShip(url, normalized, requestBody)) return;
				if (TryHandleKdock(url, normalized)) return;
				if (TryHandleGetShip(url, normalized)) return;

				if (TryHandleRemodelSlot(url, normalized, requestBody)) return;
				if (TryHandleBattleResult(url, normalized)) return;

				// 入渠系
				if (TryHandleNyukyoStart(url, normalized, requestBody)) return;
				if (TryHandleNyukyoSpeedChange(url, normalized, requestBody)) return;
				if (TryHandleNdockList(url, normalized)) return;

				// 基地航空隊
				if (TryHandleAirCorpsSupply(url, normalized)) return;
				if (TryHandleSetPlane(url, normalized, requestBody)) return;
				if (TryHandleAirCorpsChangeOrSet(url, normalized, requestBody)) return;
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
			catch
			{
			}
		}

		/// <summary>
		/// 出撃開始 (api_req_map/start)
		/// </summary>
		private bool TryHandleMapStart(string url, string requestBody, string normalized)
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
							this.sortieDeckIds.Add(deckId);

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

				// SortieInfo の更新（出撃開始）- cellNo は取得するがキャッシュするのみ
				try
				{
					if (!string.IsNullOrEmpty(normalized))
					{
						var root = JToken.Parse(normalized);
						var data = root["api_data"] ?? root;
						if (data != null)
						{
							int mapAreaId = data["api_maparea_id"]?.Value<int>() ?? 0;
							int mapInfoNo = data["api_mapinfo_no"]?.Value<int>() ?? 0;
							int cellNo = data["api_no"]?.Value<int>() ?? 0;

							// cellNo をキャッシュ（battle 時に使用）
							this.cachedCellNo = cellNo;

							if (mapAreaId > 0 && mapInfoNo > 0)
							{
								RunOnUi(() =>
								{
									try
									{
										// cellNo パラメータを渡さない（表示しない）
										this.SortieInfo.Start(mapAreaId, mapInfoNo, 0);
									}
									catch { }
								});
							}
						}
					}
				}
				catch { }

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

		/// <summary>
		/// 次の海域へ進撃 (api_req_map/next)
		/// </summary>
		private bool TryHandleMapNext(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_map/next")) return false;

			try
			{
				if (!string.IsNullOrEmpty(normalized))
				{
					var root = JToken.Parse(normalized);
					var data = root["api_data"] ?? root;
					if (data != null)
					{
						int cellNo = data["api_no"]?.Value<int>() ?? 0;

						// cellNo をキャッシュ（battle 時に使用）するが、表示は更新しない
						if (cellNo > 0)
						{
							this.cachedCellNo = cellNo;
						}
					}
				}
			}
			catch { }

			return true;
		}

		/// <summary>
		/// 戦闘開始（各種 battle API）
		/// </summary>
		private bool TryHandleBattle(string url, string normalized)
		{
			// battle 系 API をまとめて判定
			if (!(url.Contains("/kcsapi/api_req_sortie/battle")
				|| url.Contains("/kcsapi/api_req_battle_midnight")
				|| url.Contains("/kcsapi/api_req_combined_battle/")))
				return false;

			// battleresult は別ハンドラで処理するため除外
			if (url.Contains("battleresult")) return false;

			// キャッシュされた cellNo を使用して表示開始
			RunOnUi(() =>
			{
				try
				{
					if (this.cachedCellNo > 0)
					{
						this.SortieInfo.EnterBattle(this.cachedCellNo);
					}
				}
				catch { }
			});

			return true;
		}


		/// <summary>
		/// 戦闘結果　BattleResult
		/// </summary>
		private bool TryHandleBattleResult(string url, string normalized)
		{
			if (!(url.Contains("/kcsapi/api_req_sortie/battleresult") || url.Contains("/kcsapi/api_req_combined_battle/battleresult"))) return false;

			// ローカル変数として型付きで宣言
			Models.Raw.kcsapi_battleresult brLocal = null;
			Models.Raw.kcsapi_combined_battle_battleresult cbrLocal = null;

			// 解析は試すが、主目的は UI の強制再描画
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_battleresult>(normalized, out brLocal))
				{
				}
				else if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_combined_battle_battleresult>(normalized, out cbrLocal))
				{
				}
				else
				{
				}
			}
			catch
			{
			}

			// 戦闘結果の WinRank を SortieInfo に反映（RunOnUi の外で先に取得）
			string winRank = null;
			try
			{
				if (brLocal != null)
				{
					winRank = brLocal.api_win_rank;
				}
				else if (cbrLocal != null)
				{
					winRank = cbrLocal.api_win_rank;
				}
				else
				{
					// フォールバック: JSON から直接取得
					try
					{
						var root = JToken.Parse(normalized);
						var data = root["api_data"] ?? root;
						winRank = data?["api_win_rank"]?.Value<string>();
					}
					catch { }
				}
			}
			catch { }

			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					if (org == null)
					{
						return;
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
						catch
						{
						}
					}

					// 組織レベルでも明示通知
					try
					{
						this.Homeport?.Organization?.NotifyUpdated();
					}
					catch
					{
					}

					// UI スレッドキューの低優先度で再通知を行う
					try
					{
						if (Application.Current != null && Application.Current.Dispatcher != null)
						{
							Application.Current.Dispatcher.InvokeAsync(() =>
							{
								try
								{
									this.Homeport?.Organization?.NotifyUpdated();
								}
								catch
								{
								}
							}, System.Windows.Threading.DispatcherPriority.Background);
						}
					}
					catch
					{
					}

					// WinRank を SortieInfo に反映
					if (!string.IsNullOrEmpty(winRank))
					{
						try
						{
							this.SortieInfo.SetBattleResult(winRank);
						}
						catch { }
					}
				}
				catch
				{
				}
			});

			return true;
		}

		/// <summary>
		/// 基地航空隊
		/// </summary>
		private bool TryHandleMapInfo(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/mapinfo")) return false;

			try
			{
				JToken root = null;
				try { root = JToken.Parse(normalized); } catch { root = null; }
				var data = root?["api_data"] ?? root;
				if (data == null) return true;

				var airBaseTok = data["api_air_base"] ?? data.SelectToken("api_air_base");
				if (airBaseTok == null) return true;

				var expandedTok = data["api_air_base_expanded_info"] ?? data.SelectToken("api_air_base_expanded_info");

				kcsapi_air_base[] ab = null;
				kcsapi_air_base_expanded_info[] abi = null;

				try { ab = airBaseTok.ToObject<kcsapi_air_base[]>(); } catch { ab = null; }
				try { abi = expandedTok?.ToObject<kcsapi_air_base_expanded_info[]>(); } catch { abi = null; }

				if (ab != null)
				{
					RunOnUi(() =>
					{
						try
						{
							// Homeport が未初期化の可能性があるので安全に作成してから反映
							if (this.Homeport == null) this.Homeport = new Homeport(this.Proxy ?? (this.Proxy = new KanColleProxy()));
							this.Homeport?.AirBases?.Update(ab, abi);

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

		/// <summary>
		/// 母港
		/// </summary>
		private bool TryHandlePort(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_port/port")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_port>(normalized, out var port))
				{
					// JSON 側も柔軟にパースして api_slot_item 等を探すためのトークンを準備
					JToken root = null;
					JToken dataTok = null;
					try { root = JToken.Parse(normalized); dataTok = root["api_data"] ?? root; } catch { root = null; dataTok = null; }

					RunOnUi(() =>
					{
						try
						{
							// Homeport が未初期化の場合は安全に作成する
							if (this.Homeport == null)
							{
								try
								{
									this.Homeport = new Homeport(this.Proxy ?? (this.Proxy = new KanColleProxy()));
								}
								catch
								{
									// 初期化に失敗したら以降の処理をスキップ
									return;
								}
							}

							if (port.api_basic != null) this.Homeport.UpdateAdmiral(port.api_basic);
							if (port.api_ship != null) this.Homeport.Organization.Update(port.api_ship);
							if (port.api_ndock != null) this.Homeport.Repairyard.Update(port.api_ndock);
							if (port.api_deck_port != null) this.Homeport.Organization.Update(port.api_deck_port);

							this.Homeport.Organization.Combined = port.api_combined_flag != 0;

							if (port.api_material != null) this.Homeport.Materials.Update(port.api_material);

							// 追加: JSON に api_slot_item が含まれている場合は Itemyard を更新する（kcsapi_port に未定義のため JToken 経由）
							try
							{
								JToken slotTok = null;
								if (dataTok != null)
								{
									slotTok = dataTok["api_slot_item"] ?? dataTok.SelectToken("api_slot_item");
								}
								// さらに root 直下を試す（念のためのフォールバック）
								if (slotTok == null && root != null)
								{
									slotTok = root["api_slot_item"] ?? root.SelectToken("api_slot_item");
								}

								if (slotTok != null && slotTok.Type == JTokenType.Array)
								{
									try
									{
										var slotItems = slotTok.ToObject<kcsapi_slotitem[]>();
										if (slotItems != null)
										{
											this.Homeport.Itemyard.Update(slotItems);
											// 内部通知が必要なら呼び出す（安全側）
											try
											{
												var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
												mi?.Invoke(this.Homeport?.Itemyard, null);
											}
											catch { }
										}
									}
									catch { }
								}
							}
							catch { }

							// UI バインディングが更新されないケースに備え、明示的に通知を出す
							try
							{
								this.Homeport?.Organization?.NotifyUpdated();
							}
							catch
							{
							}

							// 各艦隊を明示的に再計算・再通知して UI を確実に更新
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
										catch { }
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

						// 出撃していたデッキを復帰させる処理とグローバル出撃フラグ更新
						try
						{
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								var returning = this.sortieDeckIds.Intersect(org.Fleets.Keys).ToArray();
								foreach (var returningDeckId in returning)
								{
									try
									{
										org.Fleets[returningDeckId].Homing();
									}
									catch { }
									this.sortieDeckIds.Remove(returningDeckId);
								}
							}

							this.IsInSortie = this.sortieDeckIds.Count > 0;
						}
						catch
						{
						}

						// SortieInfo をリセット
						try
						{
							this.SortieInfo.Reset();
						}
						catch { }
					});
				}
				else
				{
					// 解析失敗でも true を返してハンドリング済みとする（既存ハンドラと同様の挙動）
				}
			}
			catch
			{
			}

			return true;
		}

		/// <summary>
		/// 任務完了 + 資源・アイテム更新
		/// </summary>
		private bool TryHandleClearItemGet(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_quest/clearitemget")) return false;

			try
			{
				JToken root;
				try { root = JToken.Parse(normalized); } catch { return true; }
				var data = root["api_data"] ?? root;
				if (data == null) return true;

				// api_material: int[] または api_get_material など、安定的に取得できる場合は Materials を更新
				int[] apiMaterialArray = null;
				var matTok = data["api_material"] ?? data["api_get_material"];
				if (matTok != null && matTok.Type == JTokenType.Array)
				{
					try
					{
						apiMaterialArray = matTok.Select(t => (int?)t ?? 0).ToArray();
					}
					catch { apiMaterialArray = null; }
				}

				// 装備枠の増加を推測して即時反映
				try
				{
					var bonusTok = data["api_bounus"] ?? data["api_bonus"];
					if (bonusTok != null && bonusTok.Type == JTokenType.Array)
					{
						int deltaCapacity = 0;
						foreach (var b in bonusTok.Children())
						{
							try
							{
								// 安全にフィールドを抽出（api_count / api_type / api_item.api_id 等）
								var typeTok = b["api_type"];
								var countTok = b["api_count"];
								var itemTok = b["api_item"] ?? b["api_item_id"];

								int type = typeTok?.Value<int>() ?? -1;
								int count = countTok?.Value<int>() ?? 0;
								int itemId = 0;
								if (itemTok != null)
								{
									// api_item がオブジェクトの場合と単純値の場合の両対応
									if (itemTok.Type == JTokenType.Object)
										itemId = itemTok["api_id"]?.Value<int>() ?? 0;
									else if (itemTok.Type == JTokenType.Integer)
										itemId = itemTok.Value<int>();
								}

								// ヒューリスティック:
								// - api_type == 13 は装備関連のボーナスである可能性が高い（サンプル参照）
								// - または既知の bonus api_id (例: 901/902) を個別に扱う
								if (itemId == 901)
									deltaCapacity += 1;  // 901 は固定 +1
								else if (itemId == 902)
									deltaCapacity += 2;  // 902 は固定 +2（もしそういう仕様なら）
								else if (type == 13)
									deltaCapacity += Math.Max(0, count);  // その他は count を使用
							}
							catch { /* swallow */ }
						}

						if (deltaCapacity > 0)
						{
							// Admiral.api_max_slotitem を安全に増加させて UI に即時反映する
							RunOnUi(() =>
							{
								try
								{
									var adm = this.Homeport?.Admiral;
									if (adm == null) return;

									// kcsapi_basic をクローンして api_max_slotitem を増加させ、Homeport.UpdateAdmiral で置換する。
									// 直接 RawData を書き換えるより安定して通知が飛ぶ。
									try
									{
										var json = JsonConvert.SerializeObject(adm.RawData);
										var cloned = JsonConvert.DeserializeObject<Models.Raw.kcsapi_basic>(json);
										if (cloned != null)
										{
											cloned.api_max_slotitem = (cloned.api_max_slotitem) + deltaCapacity;
											this.Homeport.UpdateAdmiral(cloned);
										}
									}
									catch { /* swallow */ }
								}
								catch { /* swallow */ }
							});
						}
					}
				}
				catch { /* swallow */ }

				// UI スレッドで安全に反映
				if (apiMaterialArray != null)
				{
					RunOnUi(() =>
					{
						try
						{
							var materials = this.Homeport?.Materials;
							if (materials != null)
							{
								// clearitemget の api_material は「増分」で来ることがあるため、
								// 現在値に加算してから Materials.Update(int[]) の private メソッドを呼ぶ。
								// (元実装は増分をそのまま渡していたため「145061 -> 200 -> 145261」のように一時的に増分だけが表示されてしまっていた)
								int[] abs;
								// 安全に index を扱う（api が長さ4でない場合はフォールバック）
								if (apiMaterialArray.Length >= 4)
								{
									abs = new int[4];
									abs[0] = materials.Fuel + apiMaterialArray[0];
									abs[1] = materials.Ammunition + apiMaterialArray[1];
									abs[2] = materials.Steel + apiMaterialArray[2];
									abs[3] = materials.Bauxite + apiMaterialArray[3];
								}
								else
								{
									// 長さが不正なら既存の Update を呼ばず、表示更新だけ行う（安全側）
									abs = null;
								}

								if (abs != null)
								{
									var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
									mi?.Invoke(materials, new object[] { abs });
								}
							}
						}
						catch { }
					});
				}

				// ボーナスアイテム(api_bounus) は後続の /api_get_member/slot_item 等で反映されることが多い。
				// 複雑なパターンは別ハンドラに任せるためここでは UI 更新を促すだけにとどめる。
				RunOnUi(() =>
				{
					try
					{
						this.Homeport?.Organization?.NotifyUpdated();
					}
					catch { }
				});
			}
			catch { }

			return true;
		}

		/// <summary>
		/// 資源
		/// </summary>
		private bool TryHandleMaterial(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/material")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_material[]>(normalized, out var mats))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport?.Materials.Update(mats);
						}
						catch { }
					});
				}
			}
			catch { }

			return true;
		}

		/// <summary>
		/// アイテム使用
		/// </summary>
		private bool TryHandleUseItem(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/useitem")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_useitem[]>(normalized, out var useitems))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport?.Itemyard.Update(useitems);
						}
						catch { }
					});
				}
			}
			catch { }

			return true;
		}

		/// <summary>
		/// 装備廃棄
		/// </summary>
		private bool TryHandleDestroyItem2(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kousyou/destroyitem2")) return false;

			try
			{
				// 型付きで試す
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_destroyitem2>(normalized, out var di))
				{
					RunOnUi(() =>
					{
						try
						{
							// api_get_material を増分として扱い、現在の Materials に加算して反映する
							var apiMat = di?.api_get_material;
							if (apiMat != null && apiMat.Length >= 4)
							{
								try
								{
									var materials = this.Homeport?.Materials;
									if (materials != null)
									{
										var abs = new int[4];
										abs[0] = materials.Fuel + (apiMat.Length > 0 ? apiMat[0] : 0);
										abs[1] = materials.Ammunition + (apiMat.Length > 1 ? apiMat[1] : 0);
										abs[2] = materials.Steel + (apiMat.Length > 2 ? apiMat[2] : 0);
										abs[3] = materials.Bauxite + (apiMat.Length > 3 ? apiMat[3] : 0);

										var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
										mi?.Invoke(materials, new object[] { abs });
									}
								}
								catch
								{
								}
							}

							// requestBody に api_slotitem_ids があれば装備を削除（CEF 経路であれば Itemyard の更新が届かないケースに対応）
							if (!string.IsNullOrEmpty(requestBody))
							{
								try
								{
									var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
									foreach (var pair in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
									{
										var kv = pair.Split(new[] { '=' }, 2);
										if (kv.Length == 2)
										{
											try { dict[kv[0]] = Uri.UnescapeDataString(kv[1]); } catch { dict[kv[0]] = kv[1]; }
										}
									}

									if (dict.TryGetValue("api_slotitem_ids", out var idsStr) && !string.IsNullOrEmpty(idsStr))
									{
										var parts = idsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
										foreach (var p in parts)
										{
											if (int.TryParse(p, out var id))
											{
												try
												{
													// MemberTable.Remove が利用可能であれば直接削除
													this.Homeport?.Itemyard?.SlotItems?.Remove(id);
												}
												catch
												{
												}
											}
										}

										// Itemyard の内部通知を呼び出す（private メソッドをリフレクションで呼ぶ）
										try
										{
											var iy = this.Homeport?.Itemyard;
											if (iy != null)
											{
												var mi2 = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
												mi2?.Invoke(iy, null);
											}
										}
										catch
										{
										}
									}
								}
								catch
								{
								}
							}

							// UI 再評価を促す
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

		/// <summary>
		/// 解体
		/// </summary>
		private bool TryHandleDestroyShip(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kousyou/destroyship")) return false;

			try
			{
				// まずレスポンス側の api_material を解析（サーバ返却の形式に応じて扱う）
				int[] apiMat = null;
				try
				{
					var root = JToken.Parse(normalized);
					var data = root["api_data"] ?? root;
					var matTok = data?["api_material"];
					if (matTok != null && matTok.Type == JTokenType.Array)
					{
						apiMat = matTok.Select(t => (int?)t ?? 0).ToArray();
					}
				}
				catch
				{
					apiMat = null;
				}

				// api_unset_list の有無を確認（存在すれば「保管」扱い）
				bool hasUnsetList = false;
				try
				{
					var root = JToken.Parse(normalized);
					var data = root["api_data"] ?? root;
					var unset = data?["api_unset_list"];
					if (unset != null && unset.HasValues) hasUnsetList = true;
				}
				catch
				{
					hasUnsetList = false;
				}

				// requestBody から解体対象艦 ID を取得
				var shipIds = new List<int>();
				if (!string.IsNullOrEmpty(requestBody))
				{
					try
					{
						var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
						var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						foreach (var p in pairs)
						{
							var kv = p.Split(new[] { '=' }, 2);
							if (kv.Length != 2) continue;
							try { dict[kv[0]] = Uri.UnescapeDataString(kv[1]); } catch { dict[kv[0]] = kv[1]; }
						}

						if (dict.TryGetValue("api_ship_id", out var ids) && !string.IsNullOrEmpty(ids))
						{
							foreach (var part in ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
							{
								if (int.TryParse(part, out var id)) shipIds.Add(id);
							}
						}
					}
					catch { }
				}

				// UI スレッドで反映
				RunOnUi(() =>
				{
					try
					{
						// 資源の反映
						// サーバから返ってくる api_material は「現在の絶対値」を返すことがあるため、
						// 増分と誤認して現在値に加算すると二重加算になる。
						// ここでは api_material を受け取ったらそのまま Materials.Update(int[]) を呼んで上書きする。
						if (apiMat != null && apiMat.Length >= 4)
						{
							try
							{
								var materials = this.Homeport?.Materials;
								if (materials != null)
								{
									var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
									// サーバ値をそのまま渡す（絶対値更新）
									mi?.Invoke(materials, new object[] { apiMat });
								}
							}
							catch { }
						}

						// 解体対象の艦を Organization から削除
						try
						{
							var org = this.Homeport?.Organization;
							if (org != null && shipIds.Count > 0)
							{
								foreach (var shipId in shipIds)
								{
									try
									{
										var ship = org.Ships?[shipId];
										if (ship == null)
										{
											// ID 指定だが既に削除済みか存在しない場合は MemberTable から直接 Remove を試す
											org.Ships.Remove(shipId);
											continue;
										}

										// 保管（api_unset_list がある）なら装備は残す -> Itemyard.RemoveFromShip を呼ばない
										if (!hasUnsetList)
										{
											// 装備も一緒に消える場合
											try { this.Homeport?.Itemyard?.RemoveFromShip(ship); } catch { }
										}
										// いずれにせよ Ship 自体は削除
										try { org.Ships.Remove(ship); }
										catch
										{
											// MemberTable.Remove(Ship) のオーバーロードがなければ id で削除
											try { org.Ships.Remove(shipId); } catch { }
										}
									}
									catch { }
								}

								// 艦娘一覧の変更通知
								try { var mi2 = org.GetType().GetMethod("RaiseShipsChanged", BindingFlags.Instance | BindingFlags.NonPublic); mi2?.Invoke(org, null); }
								catch
								{
									// フォールバック: NotifyUpdated
									try { org.NotifyUpdated(); } catch { }
								}
							}
						}
						catch { }

						// 装備数・組織の UI 再評価
						try { this.Homeport?.Itemyard?.GetType().GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(this.Homeport?.Itemyard, null); } catch { }
						try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
					}
					catch { }
				});
			}
			catch { }

			return true;
		}

		/// <summary>
		/// 近代化改装
		/// </summary>
		private bool TryHandlePowerup(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/powerup")) return false;

			JToken root;
			try { root = JToken.Parse(normalized); }
			catch
			{
				// 解析失敗は安全に終了（既存の挙動を維持）
				return true;
			}
			var data = root["api_data"] ?? root;
			if (data == null) return true;

			var shipTok = data["api_ship"] ?? data["api_ship_data"];
			var deckTok = data["api_deck"];
			var unsetListTok = data["api_unset_list"]; // 装備解除時に返るケースあり

			// 装備配列が来ている可能性を探す（api_slot_item / api_slotitem 等）
			JToken slotTok = null;
			try
			{
				slotTok = data["api_slot_item"] ?? data["api_slotitem"] ?? root["api_slot_item"] ?? root["api_slotitem"];
			}
			catch { slotTok = null; }

			// requestBody から api_id_items を取り出して削除対象ID配列を用意する（CEF 経路で使う）
			// 注: powerup の api_id_items は「改修素材にした艦の ID」の場合があるため、実行時に艦テーブルに存在するかで判定
			int[] apiIdItemsRaw = null;
			if (!string.IsNullOrEmpty(requestBody))
			{
				try
				{
					var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					foreach (var pair in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
					{
						var kv = pair.Split(new[] { '=' }, 2);
						if (kv.Length != 2) continue;
						try { dict[kv[0]] = Uri.UnescapeDataString(kv[1]); } catch { dict[kv[0]] = kv[1]; }
					}

					if (dict.TryGetValue("api_id_items", out var idsStr) && !string.IsNullOrEmpty(idsStr))
					{
						try
						{
							apiIdItemsRaw = idsStr
								.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
								.Select(s => { int v; return int.TryParse(s, out v) ? v : 0; })
								.Where(v => v > 0)
								.ToArray();
						}
						catch { apiIdItemsRaw = null; }
					}
				}
				catch { apiIdItemsRaw = null; }
			}

			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					if (org == null) return;

					var updatedShipIds = new List<int>();

					// 1) api_ship を柔軟にハンドル（単一 or 配列）
					try
					{
						if (shipTok != null)
						{
							if (shipTok.Type == JTokenType.Array)
							{
								var rawShips = shipTok.ToObject<kcsapi_ship2[]>();
								if (rawShips != null)
								{
									foreach (var raw in rawShips)
									{
										if (raw == null) continue;
										try
										{
											var existing = org.Ships?[raw.api_id];
											if (existing != null) existing.Update(raw);
											else this.Homeport.Organization.Update(new[] { raw });
											updatedShipIds.Add(raw.api_id);
										}
										catch { }
									}
								}
							}
							else if (shipTok.Type == JTokenType.Object)
							{
								var raw = shipTok.ToObject<kcsapi_ship2>();
								if (raw != null)
								{
									try
									{
										var existing = org.Ships?[raw.api_id];
										if (existing != null) existing.Update(raw);
										else this.Homeport.Organization.Update(new[] { raw });
										updatedShipIds.Add(raw.api_id);
									}
									catch { }
								}
							}
						}
					}
					catch { }

					// 2) デッキ更新（配列 / 単一）
					try
					{
						if (deckTok != null)
						{
							if (deckTok.Type == JTokenType.Array)
							{
								var decks = deckTok.ToObject<kcsapi_deck[]>();
								if (decks != null)
								{
									foreach (var d in decks) try { this.Homeport.Organization.Update(d); } catch { }
								}
							}
							else if (deckTok.Type == JTokenType.Object)
							{
								var deck = deckTok.ToObject<kcsapi_deck>();
								if (deck != null) try { this.Homeport.Organization.Update(deck); } catch { }
							}
						}
					}
					catch { }

					// 3) 装備アイテム更新：api_slot_item / api_slotitem があれば Itemyard を更新
					try
					{
						var iy = this.Homeport?.Itemyard;
						if (slotTok != null && slotTok.Type == JTokenType.Array)
						{
							try
							{
								var slotItems = slotTok.ToObject<kcsapi_slotitem[]>();
								if (slotItems != null)
								{
									// 既存ハンドラに合わせ Update を呼ぶ
									iy?.Update(slotItems);

									// 内部通知を確実に発行
									try
									{
										var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
										mi?.Invoke(this.Homeport?.Itemyard, null);
									}
									catch { }
								}
							}
							catch { }
						}
						else if (slotTok != null && slotTok.Type == JTokenType.Object)
						{
							try
							{
								var single = slotTok.ToObject<kcsapi_slotitem>();
								if (single != null)
								{
									iy?.Update(new[] { single });
									try
									{
										var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
										mi?.Invoke(this.Homeport?.Itemyard, null);
									}
									catch { }
								}
							}
							catch { }
						}

						// 追加: requestBody にあった api_id_items を「艦 ID のみ」として扱う（Organization.Powerup と同様）。
						// 装備 ID を直接削除しない（装備解除後の api_unset_list による誤削除防止）。
						if (apiIdItemsRaw != null && apiIdItemsRaw.Length > 0)
						{
							try
							{
								var shipsToRemove = new List<Ship>();
								try
								{
									foreach (var id in apiIdItemsRaw)
									{
										try
										{
											if (org.Ships.ContainsKey(id))
											{
												var s = org.Ships[id];
												if (s != null) shipsToRemove.Add(s);
											}
										}
										catch { }
									}
								}
								catch { }

								var isUnsetList = unsetListTok != null;

								foreach (var ship in shipsToRemove)
								{
									try
									{
										// 装備解除フラグがある場合は Itemyard から削除しない（装備が母港へ戻っただけのケース）
										if (!isUnsetList)
										{
											try { this.Homeport?.Itemyard?.RemoveFromShip(ship); } catch { }
										}
										else
										{
											// 装備解除時はスロットを再同期して UI の喪失を防ぐ
											try { ship.UpdateSlots(); } catch { }
										}

										// api_id_items が実際に艦の ID を表す場合は艦自体を Organization から削除する
										try { org.Ships.Remove(ship); }
										catch
										{
											try { org.Ships.Remove(ship.Id); } catch { }
										}
									}
									catch { }
								}

								// Itemyard の再描画通知（装備解除時は必須、通常時も保険として呼ぶ）
								try
								{
									var iy2 = this.Homeport?.Itemyard;
									var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
									mi?.Invoke(iy2, null);
								}
								catch { }

								// 艦娘一覧の変更通知（既存実装に合わせる）
								try
								{
									var mi2 = org.GetType().GetMethod("RaiseShipsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
									mi2?.Invoke(org, null);
								}
								catch { try { org.NotifyUpdated(); } catch { } }
							}
							catch { }
						}
					}
					catch { }

					// 4) api_unset_list: 無条件削除は避け、装備一覧の再描画通知のみ行う（安全側）
					try
					{
						if (unsetListTok != null)
						{
							try
							{
								var iy = this.Homeport?.Itemyard;
								if (iy != null)
								{
									var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
									mi?.Invoke(iy, null);
								}
							}
							catch { }
						}
					}
					catch { }

					try
					{
						var sb = new System.Text.StringBuilder();
						sb.AppendFormat("TryHandlePowerup: slotTok={0}, unsetList={1}, api_id_items={2}, slotItemsCount={3}",
							slotTok != null, unsetListTok != null, apiIdItemsRaw?.Length ?? 0, this.Homeport?.Itemyard?.SlotItems?.Count ?? -1);

						// 各艦の slot に対して Itemyard に存在するかを列挙（問題特定用）
						try
						{
							foreach (var s in org.Ships.Values)
							{
								try
								{
									var ids = s.RawData.api_slot ?? new int[0];
									foreach (var id in ids)
									{
										if (id <= 0) continue;
										bool has = this.Homeport?.Itemyard?.SlotItems?.ContainsKey(id) ?? false;
									}
								}
								catch { }
							}
						}
						catch { }
					}
					catch { }

					// 5) 影響艦隊のみ再計算・再通知（ship 更新反映）
					try
					{
						if (updatedShipIds.Count > 0)
						{
							var affectedFleets = org.Fleets.Values
								.Where(f => f.Ships.Any(s => s != null && updatedShipIds.Contains(s.Id)))
								.ToArray();

							foreach (var f in affectedFleets)
							{
								try { f.State.Update(); } catch { }
								try { f.State.Calculate(); } catch { }
								try { f.RaiseShipsUpdated(); } catch { }
							}
						}
					}
					catch { }

					// 最終保険: Itemyard の現在状態に合わせ艦娘の Slot を再構築して UI を確実に再同期する（装備消失の回避策）
					try
					{
						foreach (var s in org.Ships.Values)
						{
							try { s.UpdateSlots(); } catch { }
						}
						foreach (var f in org.Fleets.Values)
						{
							try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
						}
					}
					catch { }

					// 6) 組織・UI レベルの最終通知
					try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
					try
					{
						var mi = org?.GetType().GetMethod("RaiseShipsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
						mi?.Invoke(org, null);
					}
					catch { try { org?.NotifyUpdated(); } catch { } }
				}
				catch { }
			});

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
						try
						{
							this.Homeport.Quests.Update(questlist);
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
		/// 艦娘の情報更新
		/// </summary>
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
							var org = this.Homeport?.Organization;
							if (org == null)
							{
								// 組織が未初期化なら既存のルートで反映
								try { this.Homeport.Organization.Update(ships); } catch { }
								return;
							}

							var updatedIds = new HashSet<int>();

							// 既存インスタンスがあれば直接 Update を呼び、なければ Organization.Update に任せる
							var toCreate = new List<Models.Raw.kcsapi_ship2>();
							foreach (var raw in ships)
							{
								if (raw == null) continue;
								updatedIds.Add(raw.api_id);

								var existing = org.Ships?[raw.api_id];
								if (existing != null)
								{
									try
									{
										// 直接既存インスタンスを更新して通知を発火させる（確実な UI 更新）
										existing.Update(raw);
									}
									catch
									{
										// 個別失敗は記録せずフォールバック
										toCreate.Add(raw);
									}
								}
								else
								{
									toCreate.Add(raw);
								}
							}

							// 新規の Ship 情報はまとめて Organization.Update に任せる
							if (toCreate.Count > 0)
							{
								try { this.Homeport.Organization.Update(toCreate.ToArray()); } catch { }
							}

							// 影響を受ける艦隊のみ再計算・再通知
							if (updatedIds.Count > 0)
							{
								var affectedFleets = org.Fleets.Values
									.Where(f => f.Ships.Any(s => s != null && updatedIds.Contains(s.Id)))
									.ToArray();

								foreach (var f in affectedFleets)
								{
									try { f.State.Update(); } catch { }
									try { f.State.Calculate(); } catch { }
									try { f.RaiseShipsUpdated(); } catch { }
								}
							}

							// 組織レベルの再通知で DataTemplate 等の再評価を促す
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

		/// <summary>
		/// 改装系3 他艦 装備スロット解除
		/// </summary>
		private bool TryHandleSlotDeprive(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/slot_deprive")) return false;

			JToken root;
			try { root = JToken.Parse(normalized); } catch { return true; }
			var data = root["api_data"] ?? root;
			if (data == null) return true;

			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					if (org == null) return;

					// api_ship_data.api_unset_ship / api_set_ship を個別に反映
					var shipData = data["api_ship_data"];
					var affected = new List<int>();

					if (shipData != null)
					{
						try
						{
							var unsetTok = shipData["api_unset_ship"];
							if (unsetTok != null && unsetTok.Type == JTokenType.Object)
							{
								var unsetShip = unsetTok.ToObject<kcsapi_ship2>();
								if (unsetShip != null)
								{
									var existing = org.Ships[unsetShip.api_id];
									if (existing != null)
									{
										existing.Update(unsetShip);
									}
									else
									{
										try { this.Homeport.Organization.Update(new[] { unsetShip }); } catch { }
									}
									affected.Add(unsetShip.api_id);
								}
							}
						}
						catch { }

						try
						{
							var setTok = shipData["api_set_ship"];
							if (setTok != null && setTok.Type == JTokenType.Object)
							{
								var setShip = setTok.ToObject<kcsapi_ship2>();
								if (setShip != null)
								{
									var existing = org.Ships[setShip.api_id];
									if (existing != null)
									{
										existing.Update(setShip);
									}
									else
									{
										try { this.Homeport.Organization.Update(new[] { setShip }); } catch { }
									}
									affected.Add(setShip.api_id);
								}
							}
						}
						catch { }
					}

					// 重要: api_unset_list に含まれる装備を Itemyard から削除しない。
					// 削除してしまうと装備が移動された場合に UI 側で失われるため、
					// 削除処理は廃止し、代わりに Itemyard の再描画通知のみ行う。
					try
					{
						var unsetListTok = data["api_unset_list"] ?? data.SelectToken("api_unset_list");
						var iy = this.Homeport?.Itemyard;
						if (iy != null && unsetListTok != null)
						{
							// ここでは削除せず、UI の再描画だけを促す。
							// 将来的に「装備が Inventory に戻る／移動する」などの厳密な処理が必要なら
							// 受信 JSON の他フィールド（api_slotitem 等）を使って明示的に同期する。
							try
							{
								var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
								mi?.Invoke(iy, null);
							}
							catch { }
						}
					}
					catch { }

					// 影響を受ける艦隊を再計算・再通知
					foreach (var id in affected.Distinct())
					{
						try
						{
							var fleet = org.GetFleet(id);
							if (fleet != null)
							{
								try { fleet.State.Update(); } catch { }
								try { fleet.State.Calculate(); } catch { }
								try { fleet.RaiseShipsUpdated(); } catch { }
							}
						}
						catch { }
					}

					// 組織・艦娘一覧の再通知
					try { org.NotifyUpdated(); } catch { }
					try
					{
						var mi = org.GetType().GetMethod("RaiseShipsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
						mi?.Invoke(org, null);
					}
					catch { }
				}
				catch
				{
				}
			});

			return true;
		}

		/// <summary>
		/// 改装系4 拡張スロット開放
		/// </summary>
		private bool TryHandleOpenExslot(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/open_exslot")) return false;

			// 成功レスポンスかどうかを確認
			bool isSuccess = false;
			try
			{
				var root = JToken.Parse(normalized);
				isSuccess = root["api_result"] != null && root["api_result"].Value<int>() == 1;
			}
			catch
			{
				isSuccess = false;
			}

			if (!isSuccess) return true;

			// requestBody から api_ship_id を取得
			int shipId = -1;
			if (!string.IsNullOrEmpty(requestBody))
			{
				try
				{
					var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (var p in pairs)
					{
						var kv = p.Split(new[] { '=' }, 2);
						if (kv.Length != 2) continue;
						if (kv[0] == "api_ship_id" && int.TryParse(Uri.UnescapeDataString(kv[1]), out var id))
						{
							shipId = id;
							break;
						}
					}
				}
				catch { }
			}

			if (shipId <= 0) return true;

			// UI 更新
			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					var ship = org?.Ships?[shipId];
					if (ship == null) return;

					try
					{
						// RawData に対して安全に拡張スロットを追加（末尾に -1 / 0 を付与）
						var raw = ship.RawData;

						var oldSlots = raw.api_slot ?? new int[0];
						var oldOnslots = raw.api_onslot ?? new int[0];

						var newSlots = new int[oldSlots.Length + 1];
						Array.Copy(oldSlots, newSlots, oldSlots.Length);
						newSlots[newSlots.Length - 1] = -1;

						var newOn = new int[oldOnslots.Length + 1];
						Array.Copy(oldOnslots, newOn, oldOnslots.Length);
						newOn[newOn.Length - 1] = 0;

						raw.api_slot = newSlots;
						raw.api_onslot = newOn;
						raw.api_slotnum = Math.Max(raw.api_slotnum, newSlots.Length);

						// Slot の再構築と艦隊再計算
						ship.UpdateSlots();

						var fleet = org.GetFleet(ship.Id);
						if (fleet != null)
						{
							try { fleet.State.Calculate(); } catch { }
							try { fleet.State.Update(); } catch { }
							try { fleet.RaiseShipsUpdated(); } catch { }
						}

						try { org.NotifyUpdated(); } catch { }
					}
					catch { }
				}
				catch { }
			});

			return true;
		}

		/// <summary>
		/// 改装系5 拡張スロットへの装備設定
		/// </summary>
		private bool TryHandleSlotsetEx(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/slotset_ex")) return false;

			// requestBody から api_ship_id / api_slot_ex を試しに取得
			int shipId = -1;
			int slotExId = int.MinValue;
			if (!string.IsNullOrEmpty(requestBody))
			{
				try
				{
					var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (var p in pairs)
					{
						var kv = p.Split(new[] { '=' }, 2);
						if (kv.Length != 2) continue;
						var key = kv[0];
						var val = Uri.UnescapeDataString(kv[1]);
						if (key == "api_ship_id") int.TryParse(val, out shipId);
						if (key == "api_slot_ex" || key == "api_slot_ex_id") int.TryParse(val, out slotExId);
					}
				}
				catch { }
			}

			// レスポンス側から api_data.api_slot_ex 等が来ていればそれを優先する
			try
			{
				var root = JToken.Parse(normalized);
				var data = root["api_data"] ?? root;
				if (data != null)
				{
					var tok = data["api_slot_ex"] ?? data.SelectToken("api_ship.api_slot_ex");
					if (tok != null && tok.Type == JTokenType.Integer)
					{
						int parsed = tok.Value<int>();
						slotExId = parsed;
					}

					// 要素が api_ship_data 配列の場合は該当艦から探す（保険）
					var shipData = data["api_ship_data"];
					if (shipData != null)
					{
						if (shipData.Type == JTokenType.Object)
						{
							var s = shipData;
							var idTok = s["api_id"] ?? s["api_ship_id"];
							if (idTok != null && idTok.Type == JTokenType.Integer && shipId <= 0) shipId = idTok.Value<int>();
							var exTok = s["api_slot_ex"];
							if (exTok != null && exTok.Type == JTokenType.Integer) slotExId = exTok.Value<int>();
						}
						else if (shipData.Type == JTokenType.Array && shipId > 0)
						{
							foreach (var elem in shipData.Children())
							{
								var idTok = elem["api_id"] ?? elem["api_ship_id"];
								if (idTok != null && idTok.Type == JTokenType.Integer && idTok.Value<int>() == shipId)
								{
									var exTok = elem["api_slot_ex"];
									if (exTok != null && exTok.Type == JTokenType.Integer) slotExId = exTok.Value<int>();
									break;
								}
							}
						}
					}
				}
			}
			catch { }

			if (shipId <= 0) return true;

			// UI 更新
			RunOnUi(() =>
			{
				try
				{
					var org = this.Homeport?.Organization;
					var ship = org?.Ships?[shipId];
					if (ship == null) return;

					try
					{
						if (slotExId != int.MinValue)
						{
							ship.RawData.api_slot_ex = slotExId;
						}
						else
						{
							// 情報がない場合は -1 にしておく（保険）
							ship.RawData.api_slot_ex = -1;
						}

						// Slot の再構築と艦隊再計算
						ship.UpdateSlots();

						var fleet = org.GetFleet(ship.Id);
						if (fleet != null)
						{
							try { fleet.State.Calculate(); } catch { }
							try { fleet.State.Update(); } catch { }
							try { fleet.RaiseShipsUpdated(); } catch { }
						}

						try { org.NotifyUpdated(); } catch { }
					}
					catch { }
				}
				catch { }
			});

			return true;
		}

		/// <summary>
		/// 編成系1
		/// </summary>
		private bool TryHandleDecks(string url, string normalized, string requestBody)
		{
			// 追加エンドポイントを許可：deck / deck_port に加え、編成変更系 API も扱う
			// 注意: preset_select / updatedeckname は専用ハンドラがあるためここでは除外する
			if (!(url.Contains("/kcsapi/api_get_member/deck")
			   || url.Contains("/kcsapi/api_get_member/deck_port")
			   || url.Contains("/kcsapi/api_req_hensei/change")))
				return false;

			try
			{
				// /api_req_hensei/change を優先処理（レスポンスに api_change_count だけ来るケースのフォールバック）
				if (url.Contains("/kcsapi/api_req_hensei/change"))
				{
					try
					{
						// レスポンス側の api_change_count を先に取得する
						int respChangeCount = 0;
						try
						{
							var root = JToken.Parse(normalized);
							var dataTok = root["api_data"] ?? root;
							var changeTok = dataTok?["api_change_count"];
							if (changeTok != null) int.TryParse(changeTok.ToString(), out respChangeCount);
						}
						catch { /* ignore parse errors */ }

						// requestBody があれば api_id を取り、なければ lastChangeDeckId をフォールバックで使う
						int deckId = -1;
						if (!string.IsNullOrEmpty(requestBody))
						{
							try
							{
								foreach (var pair in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
								{
									var kv = pair.Split(new[] { '=' }, 2);
									if (kv.Length != 2) continue;
									if (kv[0] == "api_id")
									{
										if (int.TryParse(Uri.UnescapeDataString(kv[1]), out var id)) { deckId = id; break; }
									}
								}
							}
							catch { }
						}

						if (deckId == -1 && this.lastChangeDeckId != -1) deckId = this.lastChangeDeckId;

						// deckId が確定していなければここでは処理しない（その他のハンドラにフォールバック）
						if (deckId == -1) { /* fallthrough to other handling below */ }
						else if (respChangeCount > 0)
						{
							// UI 更新は UI スレッドで行う
							RunOnUi(() =>
							{
								try
								{
									var org = this.Homeport?.Organization;
									if (org == null || !org.Fleets.ContainsKey(deckId)) return;
									var fleet = org.Fleets[deckId];

									int nonEmpty = fleet.Ships.Count(s => s != null && s.Id > 0);

									// ① 全解除に相当
									if (respChangeCount >= nonEmpty && nonEmpty > 0)
									{
										fleet.UnsetAll();
										fleet.RaiseShipsUpdated();
										org.NotifyUpdated();
									}
									else if (respChangeCount > 0 && nonEmpty > 0)
									{
										// ② 部分解除：レスポンスだけの場合は末尾から消えることが多いのでヒューリスティックで解除
										int toRemove = respChangeCount;
										for (int i = fleet.Ships.Length - 1; i >= 0 && toRemove > 0; i--)
										{
											var s = fleet.Ships[i];
											if (s != null && s.Id > 0)
											{
												fleet.Unset(i);
												toRemove--;
											}
										}
										fleet.RaiseShipsUpdated();
										org.NotifyUpdated();
									}
								}
								catch
								{
								}
							});

							// 既に change レスポンスを処理したので TryHandleDecks 全体として true を返す
							return true;
						}
					}
					catch
					{
					}
				}

				// まず配列として試す
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck[]>(normalized, out var decks))
				{
					RunOnUi(() =>
					{
						try
						{
							if (decks != null)
							{
								foreach (var deck in decks)
								{
									try { this.Homeport.Organization.Update(deck); } catch { }
								}
							}

							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }

							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});

					return true;
				}

				// 単一デッキの場合
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck>(normalized, out var singleDeck))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Update(singleDeck);
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});

					return true;
				}

				// 以下は requestBody ベースの従来ロジック（単一操作 / 複数 idx / 旗艦以外全解除 等）
				if (url.Contains("/kcsapi/api_req_hensei/change"))
				{
					try
					{
						var multi = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
						if (!string.IsNullOrEmpty(requestBody))
						{
							foreach (var pair in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
							{
								var kv = pair.Split(new[] { '=' }, 2);
								if (kv.Length != 2) continue;
								var key = kv[0];
								string val;
								try { val = Uri.UnescapeDataString(kv[1]); } catch { val = kv[1]; }
								if (!multi.TryGetValue(key, out var list)) { list = new List<string>(); multi[key] = list; }
								list.Add(val);
							}
						}

						int deckId = -1;
						if (multi.TryGetValue("api_id", out var apiIdList) && apiIdList.Count > 0 && int.TryParse(apiIdList[0], out var parsedId))
						{
							deckId = parsedId;
						this.lastChangeDeckId = deckId;
						}
						else if (this.lastChangeDeckId != -1)
						{
							deckId = this.lastChangeDeckId;
						}
						else
						{
							return true;
						}

						var idxs = new List<int>();
						if (multi.TryGetValue("api_ship_idx", out var idxVals))
						{
							foreach (var v in idxVals)
							{
								foreach (var part in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
								{
									if (int.TryParse(part, out var n)) idxs.Add(n);
								}
							}
						}

						var shipIds = new List<int>();
						if (multi.TryGetValue("api_ship_id", out var shipVals))
						{
							foreach (var v in shipVals)
							{
								foreach (var part in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
								{
									if (int.TryParse(part, out var n)) shipIds.Add(n);
								}
							}
						}

						int respChangeCount = 0;
						try
						{
							var root = JToken.Parse(normalized);
							var dataTok = root["api_data"] ?? root;
							var changeTok = dataTok?["api_change_count"];
							if (changeTok != null && int.TryParse(changeTok.ToString(), out var cc)) respChangeCount = cc;
						}
						catch { }

						RunOnUi(() =>
						{
							try
							{
								var org = this.Homeport?.Organization;
								if (org == null) return;
								if (!org.Fleets.ContainsKey(deckId)) return;
								var fleet = org.Fleets[deckId];

								// 1) 明示的な「旗艦以外全解除」
								if (idxs.Count == 1 && idxs[0] == -2 && (!shipIds.Any() || shipIds.All(x => x == -2)))
								{
									try { fleet.UnsetAll(); } catch { }
									try { fleet.RaiseShipsUpdated(); } catch { }
									try { org.NotifyUpdated(); } catch { }
									return;
								}

								// 2) idx 複数かつ shipIds 無 -> 各 Unset
								if (idxs.Count > 1 && !shipIds.Any())
								{
									foreach (var idx in idxs)
									{
										try { fleet.Unset(idx); } catch { }
									}
									try { fleet.RaiseShipsUpdated(); } catch { }
									try { org.NotifyUpdated(); } catch { }
									return;
								}

								// 3) requestBody に idx 情報が無いが respChangeCount が来た場合の処理
								if (idxs.Count == 0 && respChangeCount > 0)
								{
									int nonEmpty = fleet.Ships.Count(s => s != null && s.Id > 0);

									// 全解除相当なら UnsetAll
									if (respChangeCount >= nonEmpty && nonEmpty > 0)
									{
										try { fleet.UnsetAll(); } catch { }
										try { fleet.RaiseShipsUpdated(); } catch { }
										try { org.NotifyUpdated(); } catch { }
										return;
									}

									// 部分解除なら末尾から respChangeCount 個を Unset（ヒューリスティック）
									if (respChangeCount > 0 && nonEmpty > 0)
									{
										int toRemove = respChangeCount;
										for (int i = fleet.Ships.Length - 1; i >= 0 && toRemove > 0; i--)
										{
											var s = fleet.Ships[i];
											if (s != null && s.Id > 0)
											{
												try { fleet.Unset(i); } catch { }
												toRemove--;
											}
										}
										try { fleet.RaiseShipsUpdated(); } catch { }
										try { org.NotifyUpdated(); } catch { }
										return;
									}

									return;
								}

								// 4) 単一操作（従来ロジック）
								if (idxs.Count >= 1)
								{
									var idx = idxs[0];
									int shipId = shipIds.Count > 0 ? shipIds[0] : 0;

									if (idx == 0 && shipId == -2)
									{
										try { fleet.UnsetAll(); } catch { }
										try { fleet.RaiseShipsUpdated(); } catch { }
										try { org.NotifyUpdated(); } catch { }
										return;
									}

									if (shipId <= 0)
									{
										try { fleet.Unset(idx); } catch { }
										try { fleet.RaiseShipsUpdated(); } catch { }
										try { org.NotifyUpdated(); } catch { }
										return;
									}

									var ship = org.Ships[shipId];
									if (ship == null)
									{
										try { fleet.Unset(idx); } catch { }
										try { fleet.RaiseShipsUpdated(); } catch { }
										try { org.NotifyUpdated(); } catch { }
										return;
									}

									var currentFleet = org.GetFleet(ship.Id);

									if (currentFleet == null)
									{
										try { fleet.Change(idx, ship); } catch { }
									}
									else
									{
										try
										{
											var currentIndex = Array.IndexOf(currentFleet.Ships, ship);
											var old = fleet.Change(idx, ship);
											if (currentIndex >= 0)
											{
												if (old == null)
												{
													try { currentFleet.Unset(currentIndex); } catch { }
												}
												else
												{
													try { currentFleet.Change(currentIndex, old); } catch { }
												}
											}
										}
										catch { }
									}

									try { fleet.RaiseShipsUpdated(); } catch { }
									try
									{
										foreach (var f in org.Fleets.Values)
										{
											try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
										}
									}
									catch { }

									try { org.NotifyUpdated(); } catch { }
									return;
								}
							}
							catch { }
						});
					}
					catch
					{
						// swallow
					}
				}
			}
			catch
			{
			}

			return true;
		}

		/// <summary>
		/// 編成系2　プリセット編成取得
		/// </summary>
		private bool TryHandlePresetDeck(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/preset_deck")) return false;

			try
			{
				// preset_deck はデッキ配列を返す想定だが、柔軟に配列/単一両対応
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck[]>(normalized, out var decks))
				{
					RunOnUi(() =>
					{
						try
						{
							if (decks != null)
							{
								foreach (var deck in decks)
								{
									try { this.Homeport.Organization.Update(deck); } catch { }
								}
							}
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
					return true;
				}

				// 単一の kcsapi_deck の場合
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck>(normalized, out var single))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Update(single);
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
					return true;
				}
			}
			catch
			{
			}
			return true; // マッチしたが解析失敗でも早期 return（既存ハンドラと同挙動）
		}

		/// <summary>
		/// 編成系3　プリセット編成実行
		/// </summary>
		private bool TryHandlePresetSelect(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_hensei/preset_select")) return false;

			try
			{
				// まず kcsapi_deck 単一のデシリアライズを試す
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_deck>(normalized, out var deck))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Update(deck);
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }

							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
					return true;
				}

				// フォールバック: api_data に直に配列や api_ship がある場合に柔軟に処理する
				JToken root;
				try { root = JToken.Parse(normalized); } catch { return true; }
				var data = root["api_data"] ?? root;
				if (data == null) return true;

				// api_id / api_name / api_ship 等があれば組み立てて適用
				var idTok = data["api_id"] ?? data["api_deck_id"];
				var shipTok = data["api_ship"] ?? data["api_ship_list"];
				var nameTok = data["api_name"];

				if (idTok != null && shipTok != null && shipTok.Type == JTokenType.Array)
				{
					var built = new Models.Raw.kcsapi_deck
					{
						api_id = idTok.Value<int>(),
						api_name = nameTok != null ? nameTok.Value<string>() : string.Empty,
						api_ship = shipTok.Select(t => (int?)t ?? 0).ToArray()
					};
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Update(built);
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }

							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
				}
			}
			catch
			{
				// swallow
			}

			return true;
		}

		/// <summary>
		/// 編成系4　連合艦隊
		/// </summary>
		private bool TryHandleHenseiCombined(string url, string normalized)
		{
			// 柔軟にマッチさせる（タイプミスや微妙なパス差異を吸収）
			if (!url.Contains("api_req_hensei/combined")) return false;

			try
			{
				// まず強く型付きで試す
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_hensei_combined>(normalized, out var data))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport.Organization.Combined = (data?.api_combined ?? 0) != 0;
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
					return true;
				}

				// フォールバック: JSON をパースして api_data.api_combined を探す
				JToken root;
				try { root = JToken.Parse(normalized); } catch { return true; }
				var dataTok = root["api_data"] ?? root;
				var combinedTok = dataTok?["api_combined"] ?? dataTok?["api_combined_flag"] ?? dataTok?["api_combined_flg"];
				if (combinedTok == null) return true;

				int combined = 0;
				int.TryParse(combinedTok.ToString(), out combined);

				RunOnUi(() =>
				{
					try
					{
						this.Homeport.Organization.Combined = combined != 0;
						try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
						var org = this.Homeport?.Organization;
						if (org != null)
						{
							foreach (var f in org.Fleets.Values)
							{
								try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
							}
						}
					}
					catch { }
				});

				return true;
			}
			catch
			{
				// swallow
			}
			return true;
		}

		/// <summary>
		/// 編成系5　艦隊名の編集
		/// </summary>
		private bool TryHandleUpdatedeckname(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_member/updatedeckname")) return false;

			try
			{
				// レスポンスは成功 JSON のみでデータを返さないことが多いので requestBody を参照して即時反映する
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

				if (!dict.ContainsKey("api_deck_id")) return true;
				if (!int.TryParse(dict["api_deck_id"], out var deckId)) return true;
				var name = dict.ContainsKey("api_name") ? dict["api_name"] : string.Empty;

				RunOnUi(() =>
				{
					try
					{
						var org = this.Homeport?.Organization;
						if (org == null) return;
						if (!org.Fleets.ContainsKey(deckId)) return;
						var fleet = org.Fleets[deckId];

						// 直接艦隊名を更新して通知
						try { fleet.Name = name; } catch { }
						try { fleet.RaiseShipsUpdated(); } catch { }
						try { org.NotifyUpdated(); } catch { }
					}
					catch
					{
					}
				});
			}
			catch
			{
				// swallow
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

		private bool TryHandleSlotItems(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/slot_item")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_slotitem[]>(normalized, out var slotItems))
				{
					RunOnUi(() => { try { this.Homeport.Itemyard.Update(slotItems); } catch { } });
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
		/// 開発
		/// </summary>
		private bool TryHandleCreateItem(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kousyou/createitem")) return false;

			JToken root;
			try { root = JToken.Parse(normalized); } catch { return true; }
			var data = root["api_data"] ?? root;
			if (data == null) return true;

			RunOnUi(() =>
			{
				try
				{
					// 1) 資源・資材更新: api_material が 4 か 8 長配列で来る場合の柔軟対応
					try
					{
						var matTok = data["api_material"] ?? data["api_get_material"] ?? data["api_materials"];
						if (matTok != null && matTok.Type == JTokenType.Array)
						{
							var arr = matTok.Select(t => (int?)t ?? 0).ToArray();
							var materials = this.Homeport?.Materials;
							if (materials != null && arr != null)
							{
								// 長さ8なら個別プロパティを更新（0..7 のマッピングは既存の Update(kcsapi_material[]) に合わせる）
								if (arr.Length >= 8)
								{
									try
									{
										var ty = typeof(Materials);
										ty.GetProperty("Fuel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[0]);
										ty.GetProperty("Ammunition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[1]);
										ty.GetProperty("Steel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[2]);
										ty.GetProperty("Bauxite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[3]);
										// 既存コードのマッピングに合わせる：
										// index4 -> InstantBuildMaterials
										// index5 -> InstantRepairMaterials
										// index6 -> DevelopmentMaterials
										// index7 -> ImprovementMaterials
										ty.GetProperty("InstantBuildMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[4]);
										ty.GetProperty("InstantRepairMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[5]);
										ty.GetProperty("DevelopmentMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[6]);
										ty.GetProperty("ImprovementMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[7]);
									}
									catch { }
								}
								// 4 要素なら従来通り private Update(int[]) を呼ぶ
								else if (arr.Length == 4)
								{
									try
									{
										var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
										mi?.Invoke(materials, new object[] { arr });
									}
									catch { }
								}
							}
						}
					}
					catch { }

					// 2) 生成された装備を反映: api_get_items / api_slot_item / api_slotitem などの複合対応
					try
					{
						var iy = this.Homeport?.Itemyard;
						if (iy != null)
						{
							// api_get_items (軽量形式)
							var getItemsTok = data["api_get_items"] ?? data["api_get_item"] ?? data["api_get_item_list"];
							if (getItemsTok != null && getItemsTok.Type == JTokenType.Array)
							{
								try
								{
									var list = new List<kcsapi_slotitem>();
									foreach (var t in getItemsTok.Children())
									{
										try
										{
											var id = t["api_id"]?.Value<int>() ?? 0;
											var sid = t["api_slotitem_id"]?.Value<int>() ?? 0;
											if (id <= 0 || sid <= 0) continue;
											list.Add(new kcsapi_slotitem
											{
												api_id = id,
												api_slotitem_id = sid,
												api_level = t["api_level"]?.Value<int>() ?? 0,
												api_locked = t["api_locked"]?.Value<int>() ?? 0,
												api_alv = t["api_alv"]?.Value<int>() ?? 0,
											});
										}
										catch { }
									}

									if (list.Count > 0)
									{
										// 重複追加を避けつつ追加
										foreach (var raw in list)
										{
											try
											{
												if (!iy.SlotItems.ContainsKey(raw.api_id))
												{
													iy.SlotItems.Add(new SlotItem(raw));
												}
											}
											catch { }
										}

										// 通知
										try
										{
											var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
											mi?.Invoke(iy, null);
										}
										catch { }
									}
								}
								catch { }
							}

							// api_slot_item / api_slotitem (フル情報)
							var slotTok = data["api_slot_item"] ?? data["api_slotitem"] ?? root["api_slot_item"] ?? root["api_slotitem"];
							if (slotTok != null && slotTok.Type == JTokenType.Array)
							{
								try
								{
									var rawItems = slotTok.ToObject<kcsapi_slotitem[]>();
									if (rawItems != null && rawItems.Length > 0)
									{
										// 既存ハンドラに倣い Update を呼ぶケースはあるが、ここでは差分追加で扱う（CEF経路のフォールバック）
										foreach (var r in rawItems)
										{
											try
											{
												if (!iy.SlotItems.ContainsKey(r.api_id))
												{
													iy.SlotItems.Add(new SlotItem(r));
												}
												else
												{
													// 既存なら情報を更新する
													try { iy.SlotItems[r.api_id].Remodel(r.api_level, r.api_slotitem_id); } catch { }
												}
											}
											catch { }
										}

										try
										{
											var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
											mi?.Invoke(iy, null);
										}
										catch { }
									}
								}
								catch { }
							}

							// 3) api_unset_items / api_unset_list があれば UI 再描画通知（削除は慎重に行う）
							try
							{
								var unsetTok = data["api_unset_items"] ?? data["api_unset_list"] ?? data["api_unset_slot"];
								if (unsetTok != null)
								{
									try
									{
										var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
										mi?.Invoke(iy, null);
									}
									catch { }
								}
							}
							catch { }
						}
					}
					catch { }

					// 4) Dockyard / CreatedSlotItem 更新（Dockyard.CreateSlotItem と同等の反映）
					try
					{
						// kcsapi_createitem の api_slot_item が root 下にある場合、それを使って Dockyard.CreatedSlotItem と Dockyard の更新を促す
						try
						{
							var createTok = data.ToObject<kcsapi_createitem>();
							if (createTok != null)
							{
								// Dockyard 側で CreatedSlotItem 更新は proxy 経由で行われるが、CEF 経路ではここで生成情報を反映しておく
								var dockyard = this.Homeport?.Dockyard;
								if (dockyard != null)
								{
									try
									{
										// Dockyard.CreateSlotItem に相当する処理は内部 private のため簡易に CreatedSlotItem を作る
										dockyard.GetType().GetProperty("CreatedSlotItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(dockyard, new CreatedSlotItem(createTok));
									}
									catch { }
								}
							}
						}
						catch { }
					}
					catch { }

					// 最後に UI 全体更新を促す
					try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
				}
				catch { }
			});

			return true;
		}

		/// <summary>
		/// 建造系1 建造ドック ID をキャッシュ
		/// </summary>
		private bool TryHandleCreateShip(string url, string normalized, string requestBody)
		{
			if (!(url.Contains("/kcsapi/api_req_kousyou/createship") || url.Contains("/kcsapi/api_req_kousyou/createship_speedchange")))
				return false;

			if (string.IsNullOrEmpty(requestBody)) return true;

			// dict と keyId を外側スコープで宣言して後続から参照できるようにする
			Dictionary<string, string> dict = null;
			int keyId = -1;

			try
			{
				var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
				int kdockIdFound = -1;
				int[] items = null;

				// requestBody を dict にパース（ここで全てのキーを取得）
				dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var p in pairs)
				{
					var kv = p.Split(new[] { '=' }, 2);
					if (kv.Length != 2) continue;
					try { dict[kv[0]] = Uri.UnescapeDataString(kv[1]); } catch { dict[kv[0]] = kv[1]; }
				}

				// kdock id キャッシュ（可能なキー名をチェック）
				if (dict.TryGetValue("api_kdock_id", out var kdVal) || dict.TryGetValue("api_kdock", out kdVal) || dict.TryGetValue("api_kdockno", out kdVal))
				{
					if (int.TryParse(kdVal, out var id))
					{
						this.lastCreateKdockId = id;
						kdockIdFound = id;
					}
				}

				// api_item1..api_item5 を抜き出す
				var tmp = new List<int>();
				for (int i = 1; i <= 5; i++)
				{
					if (dict.TryGetValue($"api_item{i}", out var s) && int.TryParse(s, out var v))
					{
						tmp.Add(v);
					}
					else
					{
						tmp.Add(0);
					}
				}
				items = tmp.ToArray();

				// kdockId を確定して pending に保存
				keyId = kdockIdFound > 0 ? kdockIdFound : this.lastCreateKdockId;
				if (keyId > 0 && items != null)
				{
					lock (this.pendingCreateMaterials)
					{
						this.pendingCreateMaterials[keyId] = items;
					}
				}
			}
			catch
			{
				// swallow
			}

			// createship_speedchange または api_highspeed=1 の検知 -> InstantBuildMaterials を即時減算し、適用済みフラグをセット
			try
			{
				bool isSpeedChangeEndpoint = url.Contains("/createship_speedchange");
				bool highspeedFlag = dict != null && dict.TryGetValue("api_highspeed", out var hv) && hv == "1";

				if (isSpeedChangeEndpoint || highspeedFlag)
				{
					var kdId = keyId > 0 ? keyId : this.lastCreateKdockId;
					if (kdId > 0)
					{
						lock (this.appliedBuildKdock)
						{
							if (!this.appliedBuildKdock.Contains(kdId)) this.appliedBuildKdock.Add(kdId);
						}

						// UI スレッドで即時に InstantBuildMaterials を 1 減算
						RunOnUi(() =>
						{
							try
							{
								var materials = this.Homeport?.Materials;
								if (materials != null)
								{
									var propBuild = typeof(Materials).GetProperty("InstantBuildMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
									if (propBuild != null)
									{
										var cur = (int)propBuild.GetValue(materials);
										var next = Math.Max(0, cur - 1);
										propBuild.SetValue(materials, next);
									}
								}
							}
							catch (Exception)
							{
							}
						});
					}
				}
			}
			catch { }

			// createship のレスポンスに資源情報が含まれていれば既存ロジックで反映（残す）
			try
			{
				if (!string.IsNullOrEmpty(normalized))
				{
					JToken root;
					try { root = JToken.Parse(normalized); }
					catch { root = null; }

					if (root != null)
					{
						var data = root["api_data"] ?? root;
						var matTok = data?["api_material"] ?? data?["api_get_material"] ?? data?["api_materials"];
						if (matTok != null && matTok.Type == JTokenType.Array)
						{
							try
							{
								var matArr = matTok.Select(t => (int?)t ?? 0).ToArray();
								RunOnUi(() =>
								{
									try
									{
										var materials = this.Homeport?.Materials;
										if (materials != null && matArr != null && matArr.Length >= 4)
										{
											var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
											mi?.Invoke(materials, new object[] { matArr });
										}
									}
									catch { }
								});
							}
							catch { }
						}
					}
				}
			}
			catch { }

			return true;
		}

		/// <summary>
		/// 建造系2 ドック一覧
		/// </summary>
		private bool TryHandleKdock(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/kdock")) return false;

			try
			{
				// 型デシリアライズを優先
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_kdock[]>(normalized, out var kdocks))
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport?.Dockyard?.Update(kdocks);
							// 各 kdock に対して pending があれば適用
							try
							{
								foreach (var rawK in kdocks)
								{
									try { this.ApplyPendingCreateMaterialsForKdock(rawK.api_id, rawK.api_state); } catch { }
								}
							}
							catch { }

							// Dock の変化は UI に影響するため全体再通知・艦隊再計算
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
					return true;
				}

				// フォールバック: JSON をパースして api_data を探す
				JToken root;
				try { root = JToken.Parse(normalized); } catch { return true; }
				var data = root["api_data"] ?? root;
				if (data == null) return true;
				var kdockTok = data.Type == JTokenType.Array ? data : data["api_kdock"] ?? data.SelectToken("api_kdock");
				if (kdockTok == null) return true;

				var parsed = kdockTok.ToObject<kcsapi_kdock[]>();
				if (parsed != null)
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport?.Dockyard?.Update(parsed);
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
				}
			}
			catch
			{
			}
			return true;
		}

		/// <summary>
		/// 建造系3 艦娘の入手処理
		/// </summary>
		private bool TryHandleGetShip(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_kousyou/getship")) return false;

			JToken root;
			try { root = JToken.Parse(normalized); } catch { return true; }
			var data = root["api_data"] ?? root;
			if (data == null) { return true; }

			try
			{
				var kdockTok = data["api_kdock"];
				var shipTok = data["api_ship"];
				var slotTok = data["api_slotitem"];

				RunOnUi(() =>
				{
					try
					{
						var org = this.Homeport?.Organization;

						// --- kdock / 入手扱いの処理（可能なら kcsapi_kdock_getship として AddFromDock を呼ぶ） ---
						var kdockProcessed = false;
						if (kdockTok != null)
						{
							try
							{
								// Dock 表示用に従来の kcsapi_kdock[] にも更新する（UI のドック状態）
								kcsapi_kdock[] kdocks = null;
								try
								{
									kdocks = kdockTok.ToObject<kcsapi_kdock[]>();
									if (kdocks != null)
									{
										this.Homeport?.Dockyard?.Update(kdocks);
									}
								}
								catch (Exception)
								{
								}

								// kdock 配列から api_state マップを作成（kdock_getship に state が無い場合のフォールバック用）
								Dictionary<int, int> kdockStateMap = null;
								if (kdocks != null)
								{
									try { kdockStateMap = kdocks.ToDictionary(k => k.api_id, k => k.api_state); } catch { kdockStateMap = null; }
								}

								// 可能なら kcsapi_kdock_getship[] として解析し、Itemyard.AddFromDock を呼ぶ
								try
								{
									var kdocksGet = kdockTok.ToObject<kcsapi_kdock_getship[]>();
									if (kdocksGet != null && kdocksGet.Length > 0)
									{
										foreach (var kd in kdocksGet)
										{
											try
											{
												this.Homeport?.Itemyard?.AddFromDock(kd);

												// kdock_getship に api_state が無い場合があるため、kdock 配列のマップを参照して state を取得する
												int? state = null;
												try
												{
													if (kdockStateMap != null && kdockStateMap.TryGetValue(kd.api_id, out var s)) state = s;
												}
												catch { state = null; }

												// pending materials を適用（api_state を渡す）
												try { this.ApplyPendingCreateMaterialsForKdock(kd.api_id, state); } catch { }
											}
											catch (Exception)
											{
											}
										}
										kdockProcessed = true;

										// slotTok は既に上で取得済み
										try
										{
											if (slotTok != null)
											{
												var newItems = slotTok.Type == JTokenType.Array ? slotTok.ToObject<kcsapi_slotitem[]>() : null;

												if (newItems != null && newItems.Length > 0)
												{
													try
													{
														var iy = this.Homeport?.Itemyard;
														if (iy != null)
														{
															// 既存装備を重複追加しないように、未登録のものだけ追加する
															foreach (var ni in newItems)
															{
																try
																{
																	if (ni == null) continue;
																	if (!iy.SlotItems.ContainsKey(ni.api_id))
																	{
																		iy.SlotItems.Add(new SlotItem(ni));
																	}
																}
																catch (Exception)
																{
																}
															}

															// 内部通知を確実に発行
															try
															{
																var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
																mi?.Invoke(iy, null);
															}
															catch (Exception)
															{
															}
														}
														else
														{
															this.Homeport?.Itemyard?.Update(newItems);
														}
													}
													catch (Exception)
													{
													}
												}
											}
										}
										catch (Exception)
										{
										}

										// AddFromDock 後に内部通知を呼ぶ（保険）
										try
										{
											var iy = this.Homeport?.Itemyard;
											if (iy != null)
											{
												var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
												if (mi != null)
												{
													mi.Invoke(iy, null);
												}
											}
										}
										catch (Exception)
										{
										}
									}
									else
									{
									}
								}
								catch (Exception)
								{
								}
							}
							catch (Exception)
							{
							}
						}

						// --- 生成された艦を Organization に追加・更新 ---
						if (shipTok != null)
						{
							try
							{
								var ship = shipTok.ToObject<kcsapi_ship2>();
								if (ship != null)
								{
									// まず既存の Update で試す（既存艦の更新を優先）
									try
									{
										this.Homeport.Organization.Update(new[] { ship });
									}
									catch (Exception)
									{
									}

									// Organization.Update が新規追加を行わないケースに備え、
									// Ships に存在しなければ明示的に追加して通知する（CEF 経路用フォールバック）
									try
									{
										var exists = org?.Ships?[ship.api_id];
										if (exists == null)
										{
											try
											{
												org?.Ships.Add(new Ship(this.Homeport, ship));
												// private メソッド RaiseShipsChanged をリフレクションで呼ぶ
												try
												{
													var mi = org?.GetType().GetMethod("RaiseShipsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
													mi?.Invoke(org, null);
												}
												catch (Exception)
												{
													try { org?.NotifyUpdated(); } catch { }
												}
											}
											catch (Exception)
											{
											}
										}
									}
									catch (Exception)
									{
									}

									// 生成艦の属する艦隊だけ再計算（GetFleet で取得できるなら）
									try
									{
										var f = org?.GetFleet(ship.api_id);
										if (f != null) { f.State.Update(); f.State.Calculate(); f.RaiseShipsUpdated(); }
									}
									catch (Exception) { }
								}
							}
							catch (Exception)
							{
							}
						}

						// --- 装備アイテム更新（kdockProcessed が false の場合はここで処理） ---
						if (!kdockProcessed && slotTok != null)
						{
							try
							{
								var newItems = slotTok.ToObject<kcsapi_slotitem[]>();
								if (newItems != null && newItems.Length > 0)
								{
									try
									{
										var itemyard = this.Homeport?.Itemyard;
										if (itemyard != null)
										{
											this.Homeport.Itemyard.Update(newItems);
											try
											{
												var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
												if (mi != null) mi.Invoke(this.Homeport?.Itemyard, null);
											}
											catch (Exception)
											{
											}
										}
										else
										{
											this.Homeport?.Itemyard?.Update(newItems);
										}
									}
									catch (Exception)
									{
									}
								}
							}
							catch (Exception)
							{
							}
						}
						else if (kdockProcessed)
						{
						}

						// --- 組織レベルで通知して UI を更新 ---
						try { this.Homeport?.Organization?.NotifyUpdated(); } catch (Exception) { }

						// 全フリート再計算（保険）
						try
						{
							if (org != null)
							{
								foreach (var f2 in org.Fleets.Values)
								{
									try { f2.State.Calculate(); f2.State.Update(); f2.RaiseShipsUpdated(); } catch (Exception) { }
								}
							}
						}
						catch (Exception) { }
					}
					catch (Exception)
					{
					}
				});
			}
			catch (Exception)
			{
			}

			// 取得処理は完了したので true を返す
			return true;
		}

		/// <summary>
		/// 建造系4 資源消費適用
		/// </summary>
		private void ApplyPendingCreateMaterialsForKdock(int kdockId, int? api_state = null)
		{
			try
			{
				int[] req = null;
				lock (this.pendingCreateMaterials)
				{
					if (!this.pendingCreateMaterials.TryGetValue(kdockId, out req)) return;
					this.pendingCreateMaterials.Remove(kdockId);
				}

				var materials = this.Homeport?.Materials;
				if (materials == null || req == null) return;

				// 1) 燃料/弾薬/鋼材/ボーキ を差し引く（負にならないようガード）
				try
				{
					var newMat = new int[4];
					newMat[0] = Math.Max(0, materials.Fuel - (req.Length > 0 ? req[0] : 0));
					newMat[1] = Math.Max(0, materials.Ammunition - (req.Length > 1 ? req[1] : 0));
					newMat[2] = Math.Max(0, materials.Steel - (req.Length > 2 ? req[2] : 0));
					newMat[3] = Math.Max(0, materials.Bauxite - (req.Length > 3 ? req[3] : 0));

					var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
					mi?.Invoke(materials, new object[] { newMat });
				}
				catch (Exception)
				{
				}

				// 2) api_item5 は開発資材 (DevelopmentMaterials) として減算する
				try
				{
					if (req.Length > 4)
					{
						var decDev = req[4];
						if (decDev != 0)
						{
							var propDev = typeof(Materials).GetProperty("DevelopmentMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (propDev != null)
							{
								var cur = (int)propDev.GetValue(materials);
								var next = Math.Max(0, cur - decDev);
								propDev.SetValue(materials, next);
							}
						}
					}
				}
				catch (Exception)
				{
				}

				// 3) api_state による InstantBuildMaterials の減算（api_state == 3 の場合は使用とみなして -1）
				//    ただし、createship_speedchange 等ですでに即時減算済みなら二重減算しない
				try
				{
					bool alreadyApplied = false;
					lock (this.appliedBuildKdock)
					{
						if (this.appliedBuildKdock.Contains(kdockId))
						{
							alreadyApplied = true;
							this.appliedBuildKdock.Remove(kdockId);
						}
					}

					if (!alreadyApplied && api_state.HasValue && api_state.Value == 3)
					{
						var propBuild = typeof(Materials).GetProperty("InstantBuildMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (propBuild != null)
						{
							var cur = (int)propBuild.GetValue(materials);
							var next = Math.Max(0, cur - 1);
							propBuild.SetValue(materials, next);
						}
					}
				}
				catch (Exception)
				{
				}
			}
			catch (Exception)
			{
			}
		}

		/// <summary>
		/// 装備改修
		/// </summary>
		private bool TryHandleRemodelSlot(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kousyou/remodel_slot")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_remodel_slot>(normalized, out var rem))
				{
					RunOnUi(() =>
					{
						try
						{
							var iy = this.Homeport?.Itemyard;
							var materials = this.Homeport?.Materials;

							// 1) 資源反映 (api_after_material)
							try
							{
								if (rem.api_after_material != null)
								{
									var arr = rem.api_after_material;
									if (materials != null)
									{
										// 長さ8 -> 個別プロパティ更新
										if (arr.Length >= 8)
										{
											var ty = typeof(Materials);
											try
											{
												ty.GetProperty("Fuel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[0]);
												ty.GetProperty("Ammunition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[1]);
												ty.GetProperty("Steel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[2]);
												ty.GetProperty("Bauxite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[3]);
												ty.GetProperty("InstantBuildMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[4]);
												ty.GetProperty("InstantRepairMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[5]);
												ty.GetProperty("DevelopmentMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[6]);
												ty.GetProperty("ImprovementMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(materials, arr[7]);
											}
											catch { }
										}
										else if (arr.Length >= 4)
										{
											try
											{
												var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
												mi?.Invoke(materials, new object[] { new[] { arr[0], arr[1], arr[2], arr[3] } });
											}
											catch { }
										}
									}
								}
							}
							catch { }

							// 2) api_after_slot の反映（生成・改修された装備）
							try
							{
								if (rem.api_after_slot != null && iy != null)
								{
									var a = rem.api_after_slot;
									// kcsapi_slotitem に合わせて一時オブジェクトを作る
									var raw = new kcsapi_slotitem
									{
										api_id = a.api_id,
										api_slotitem_id = a.api_slotitem_id,
										api_level = a.api_level,
										api_locked = a.api_locked,
										api_alv = 0
									};

									try
									{
										if (iy.SlotItems.ContainsKey(raw.api_id))
										{
											// 既存なら Remodel を呼ぶ（UI バインディングを発火）
											try { iy.SlotItems[raw.api_id].Remodel(raw.api_level, raw.api_slotitem_id); } catch { }
										}
										else
										{
											try { iy.SlotItems.Add(new SlotItem(raw)); } catch { }
										}
									}
									catch { }

									// 通知
									try
									{
										var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
										mi?.Invoke(iy, null);
									}
									catch { }
								}
							}
							catch { }

							// 3) api_use_slot_id: 使用（消費）された装備 ID の削除（存在すれば MemberTable から削除）
							try
							{
								if (rem.api_use_slot_id != null && rem.api_use_slot_id.Length > 0 && iy != null)
								{
									foreach (var id in rem.api_use_slot_id)
									{
										try { iy.SlotItems.Remove(id); } catch { }
									}
									try
									{
										var mi = typeof(Itemyard).GetMethod("RaiseSlotItemsChanged", BindingFlags.Instance | BindingFlags.NonPublic);
										mi?.Invoke(iy, null);
									}
									catch { }
								}
							}
							catch { }

							// 最後に組織レベルの更新通知で UI を確実に再描画
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
						}
						catch { }
					});

					return true;
				}
			}
			catch { }

			return true;
		}

		/// <summary>
		/// 入渠系1 ドック一覧
		/// </summary>
		private bool TryHandleNdockList(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/ndock")) return false;

			try
			{
				// 型デシリアライズを優先
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_ndock[]>(normalized, out var ndocks))
				{
					RunOnUi(() =>
					{
						try
						{
							// Repairyard 側はそのまま更新
							this.Homeport?.Repairyard?.Update(ndocks);

							// JSON 側をパースして api_item1..4 を集計し、まだ適用していない ndock に対して一度だけ減算する
							try
							{
								JToken root = null;
								try { root = JToken.Parse(normalized); } catch { root = null; }
								var dataTok = root?["api_data"] ?? root;

								if (dataTok != null && dataTok.Type == JTokenType.Array)
								{
									var arr = (JArray)dataTok;
									var totalConsume = new int[4];
									var materials = this.Homeport?.Materials;

									lock (this.appliedRepairNdock)
									{
										for (int i = 0; i < arr.Count && i < ndocks.Length; i++)
										{
											var token = arr[i];
											if (token == null) continue;

											// ndock id を得る（raw オブジェクト / JSON 両対応）
											int ndockId = 0;
											try { ndockId = ndocks[i]?.api_id ?? token["api_id"]?.Value<int>() ?? 0; } catch { ndockId = token["api_id"]?.Value<int>() ?? 0; }
											if (ndockId == 0) continue;

											// state を確認（0: 空, 1: 修復中 など）
											int state = token["api_state"]?.Value<int>() ?? (ndocks[i]?.api_state ?? 0);

											// ndock が空 (state == 0) なら適用済みフラグをリセット（将来の再利用に備える）
											if (state == 0)
											{
												if (this.appliedRepairNdock.Contains(ndockId)) this.appliedRepairNdock.Remove(ndockId);
												continue;
											}

											// 修復中で、まだ消費を適用していなければ集計してフラグを立てる
											if (state == 1 && !this.appliedRepairNdock.Contains(ndockId))
											{
												int it1 = token["api_item1"]?.Value<int>() ?? 0;
												int it2 = token["api_item2"]?.Value<int>() ?? 0;
												int it3 = token["api_item3"]?.Value<int>() ?? 0;
												int it4 = token["api_item4"]?.Value<int>() ?? 0;

												// 少なくとも1つ消費がある場合に集計
												if (it1 != 0 || it2 != 0 || it3 != 0 || it4 != 0)
												{
													totalConsume[0] += it1;
													totalConsume[1] += it2;
													totalConsume[2] += it3;
													totalConsume[3] += it4;

													this.appliedRepairNdock.Add(ndockId);
												}
											}
										}
									}

									// 集計した消費を Materials に適用（増分として現在値から差し引く）
									try
									{
										if (materials != null && (totalConsume[0] > 0 || totalConsume[1] > 0 || totalConsume[2] > 0 || totalConsume[3] > 0))
										{
											var newMat = new int[4];
											newMat[0] = Math.Max(0, materials.Fuel - totalConsume[0]);
											newMat[1] = Math.Max(0, materials.Ammunition - totalConsume[1]);
											newMat[2] = Math.Max(0, materials.Steel - totalConsume[2]);
											newMat[3] = Math.Max(0, materials.Bauxite - totalConsume[3]);

											var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
											mi?.Invoke(materials, new object[] { newMat });
										}
									}
									catch
									{
										// 念のため swallow（Materials による反映は安全に行いたい）
									}
								}
							}
							catch
							{
							}

							// UI 側の更新呼び出し（既存の挙動を維持）
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch
						{
						}
					});

					return true;
				}

				// フォールバック: JSON をパースして api_data を探す（既存のフォールバックは維持）
				JToken rootTok;
				try { rootTok = JToken.Parse(normalized); } catch { rootTok = null; }
				var data = rootTok?["api_data"] ?? rootTok;
				if (data == null) return true;
				var ndockTok = data.Type == JTokenType.Array ? data : data["api_ndock"] ?? data.SelectToken("api_ndock");
				if (ndockTok == null) return true;

				var parsed = ndockTok.ToObject<kcsapi_ndock[]>();
				if (parsed != null)
				{
					RunOnUi(() =>
					{
						try
						{
							this.Homeport?.Repairyard?.Update(parsed);
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
							var org = this.Homeport?.Organization;
							if (org != null)
							{
								foreach (var f in org.Fleets.Values)
								{
									try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					});
				}
			}
			catch
			{
			}
			return true;
		}

		/// <summary>
		/// 入渠系2 入渠開始
		/// </summary>
		private bool TryHandleNyukyoStart(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_nyukyo/start")) return false;

			// リクエスト body から api_ship_id / api_highspeed を参照する既存処理を継承
			var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrEmpty(requestBody))
			{
				foreach (var pair in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
				{
					var kv = pair.Split(new[] { '=' }, 2);
					if (kv.Length == 2)
					{
						try { dict[kv[0]] = Uri.UnescapeDataString(kv[1]); } catch { dict[kv[0]] = kv[1]; }
					}
				}
			}

			int shipId;
			if (!dict.ContainsKey("api_ship_id") || !int.TryParse(dict["api_ship_id"], out shipId)) return true;

			bool highspeedRequested = dict.ContainsKey("api_highspeed") && dict["api_highspeed"] == "1";

			// レスポンス側の資源情報を先に解析（増分扱い api_get_material 等）
			int[] addMaterials = null;
			try
			{
				JToken root = null;
				try { root = JToken.Parse(normalized); } catch { root = null; }
				var data = root?["api_data"] ?? root;
				var matTok = data?["api_get_material"] ?? data?["api_get_materials"] ?? data?["api_get"];
				if (matTok != null && matTok.Type == JTokenType.Array)
				{
					addMaterials = matTok.Select(t => (int?)t ?? 0).ToArray();
				}
			}
			catch { addMaterials = null; }

			RunOnUi(() =>
			{
				try
				{
					var ship = this.Homeport?.Organization?.Ships?[shipId];
					if (ship == null) return;

					// 既存の動作: 高速修復なら即時修復反映
					if (highspeedRequested)
					{
						try { ship.Repair(); } catch { }
					}

					// 資源の増分反映（api_get_material 相当を増分として扱う）
					try
					{
						if (addMaterials != null && addMaterials.Length >= 4)
						{
							var materials = this.Homeport?.Materials;
							if (materials != null)
							{
								var abs = new int[4];
								abs[0] = materials.Fuel + (addMaterials.Length > 0 ? addMaterials[0] : 0);
								abs[1] = materials.Ammunition + (addMaterials.Length > 1 ? addMaterials[1] : 0);
								abs[2] = materials.Steel + (addMaterials.Length > 2 ? addMaterials[2] : 0);
								abs[3] = materials.Bauxite + (addMaterials.Length > 3 ? addMaterials[3] : 0);

								var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
								mi?.Invoke(materials, new object[] { abs });
							}
						}
					}
					catch { }

					// 高速修復材の即時減算（UI に即時反映）
					try
					{
						if (highspeedRequested)
						{
							var materials = this.Homeport?.Materials;
							if (materials != null)
							{
								var prop = typeof(Materials).GetProperty("InstantRepairMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (prop != null)
								{
									var cur = (int)prop.GetValue(materials);
									prop.SetValue(materials, Math.Max(0, cur - 1));
								}
							}
						}
					}
					catch { }

					// 所属艦隊の状態を更新
					try { this.Homeport?.Organization?.GetFleet(ship.Id)?.State.Update(); } catch { }

					// 全体 UI の再評価
					try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
				}
				catch { }
			});

			return true;
		}

		/// <summary>
		/// 入渠系3 高速修復
		/// </summary>
		private bool TryHandleNyukyoSpeedChange(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_nyukyo/speedchange")) return false;

			// requestBody から api_ndock_id を取得する既存処理
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
			if (!dict.ContainsKey("api_ndock_id")) return false;
			if (!int.TryParse(dict["api_ndock_id"], out var ndockId)) return false;

			// レスポンスの資源増分を解析
			int[] addMaterials = null;
			try
			{
				JToken root = null;
				try { root = JToken.Parse(normalized); } catch { root = null; }
				var data = root?["api_data"] ?? root;
				var matTok = data?["api_get_material"] ?? data?["api_get_materials"];
				if (matTok != null && matTok.Type == JTokenType.Array)
				{
					addMaterials = matTok.Select(t => (int?)t ?? 0).ToArray();
				}
			}
			catch { addMaterials = null; }

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

					// 資源の増分反映（増分 api_get_material）
					try
					{
						if (addMaterials != null && addMaterials.Length >= 4)
						{
							var materials = this.Homeport?.Materials;
							if (materials != null)
							{
								var abs = new int[4];
								abs[0] = materials.Fuel + (addMaterials.Length > 0 ? addMaterials[0] : 0);
								abs[1] = materials.Ammunition + (addMaterials.Length > 1 ? addMaterials[1] : 0);
								abs[2] = materials.Steel + (addMaterials.Length > 2 ? addMaterials[2] : 0);
								abs[3] = materials.Bauxite + (addMaterials.Length > 3 ? addMaterials[3] : 0);

								var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
								mi?.Invoke(materials, new object[] { abs });
							}
						}
					}
					catch { }

					// 高速修復材（speedchange は高速修復の結果なので -1 すると安全）
					try
					{
						var materials = this.Homeport?.Materials;
						if (materials != null)
						{
							var prop = typeof(Materials).GetProperty("InstantRepairMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (prop != null)
							{
								var cur = (int)prop.GetValue(materials);
								prop.SetValue(materials, Math.Max(0, cur - 1));
							}
						}
					}
					catch { }

					// UI 更新
					try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
				}
				catch { }
			});

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

		/// <summary>
		/// 基地航空隊のスロット変更
		/// </summary>
		private bool TryHandleSetPlane(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_air_corps/set_plane")) return false;

			try
			{

				// requestBody から api_area_id と api_base_id を抽出
				int areaId = -1;
				int baseId = -1;
				if (!string.IsNullOrEmpty(requestBody))
				{
					try
					{
						var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
						foreach (var p in pairs)
						{
							var kv = p.Split(new[] { '=' }, 2);
							if (kv.Length != 2) continue;
							var key = kv[0];
							var val = Uri.UnescapeDataString(kv[1]);
							if (key == "api_area_id") int.TryParse(val, out areaId);
							if (key == "api_base_id") int.TryParse(val, out baseId);
						}
					}
					catch { }
				}

				if (areaId <= 0 || baseId <= 0)
				{
					return true;
				}

				// レスポンスから api_plane_info と api_distance を抽出
				JToken root;
				try { root = JToken.Parse(normalized); } catch { return true; }
				var data = root["api_data"] ?? root;
				if (data == null) return true;

				kcsapi_plane_info[] planeInfo = null;
				ApiDistance distance = null;

				try
				{
					var planeTok = data["api_plane_info"];
					if (planeTok != null && planeTok.Type == JTokenType.Array)
					{
						planeInfo = planeTok.ToObject<kcsapi_plane_info[]>();
					}
				}
				catch { planeInfo = null; }

				try
				{
					var distanceTok = data["api_distance"];
					if (distanceTok != null && distanceTok.Type == JTokenType.Object)
					{
						distance = distanceTok.ToObject<ApiDistance>();
					}
				}
				catch { distance = null; }

				if (planeInfo == null && distance == null)
				{
					return true;
				}

				// UI スレッドで航空隊情報を一時更新
				RunOnUi(() =>
				{
					try
					{
						var airBases = this.Homeport?.AirBases;

						if (airBases == null) return;

						var airBase = airBases.AreaGroup?[areaId];

						if (airBase != null)
						{
							airBase.UpdateFromSetPlane(planeInfo, distance, baseId);
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
		/// 基地航空隊 名称・出撃状態の変更
		/// </summary>
		private bool TryHandleAirCorpsChangeOrSet(string url, string normalized, string requestBody)
		{
			if (!(url.IndexOf("/kcsapi/api_req_air_corps/change_name", StringComparison.OrdinalIgnoreCase) >= 0
				|| url.IndexOf("/kcsapi/api_req_air_corps/set_action", StringComparison.OrdinalIgnoreCase) >= 0))
				return false;

			try
			{
				// requestBody を安全にパース
				var q = System.Web.HttpUtility.ParseQueryString(requestBody ?? "");

				int ParseInt(params string[] keys)
				{
					foreach (var k in keys)
					{
						var v = q[k];
						if (!string.IsNullOrEmpty(v) && int.TryParse(v, out var n)) return n;
					}
					return 0;
				}

				int areaId = ParseInt("api_area_id", "api_area");
				int baseId = ParseInt("api_base_id", "api_baseid", "api_rid");
				int actionKind = ParseInt("api_action_kind", "api_action", "action_kind");
				var name = q["api_name"] ?? q["name"] ?? string.Empty;

				// レスポンスに api_air_base が含まれていれば、それで丸ごと更新する（より確実）
				try
				{
					if (!string.IsNullOrEmpty(normalized))
					{
						var root = JToken.Parse(normalized);
						var data = root["api_data"] ?? root;
						var airBaseTok = data?["api_air_base"] ?? data?.SelectToken("api_air_base");
						var expandedTok = data?["api_air_base_expanded_info"] ?? data?.SelectToken("api_air_base_expanded_info");

						if (airBaseTok != null)
						{
							kcsapi_air_base[] ab = null;
							kcsapi_air_base_expanded_info[] abi = null;
							try { ab = airBaseTok.ToObject<kcsapi_air_base[]>(); } catch { ab = null; }
							try { abi = expandedTok?.ToObject<kcsapi_air_base_expanded_info[]>(); } catch { abi = null; }

							if (ab != null)
							{
								RunOnUi(() =>
								{
									try { this.Homeport?.AirBases?.Update(ab, abi); }
									catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TryHandleAirCorps] Update fallback error: {ex}"); }
								});

								// 既にレスポンスで更新したので終了しても良い
								return true;
							}
						}
					}
				}
				catch { /* フォールバック失敗しても続行 */ }

				// requestBody から得られた情報で個別更新を試みる
				if (areaId > 0 && baseId > 0)
				{
					RunOnUi(() =>
					{
						try
						{
							if (url.IndexOf("/change_name", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								this.Homeport?.AirBases?.ApplyChangeName(areaId, baseId, name);
							}
							else if (url.IndexOf("/set_action", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								this.Homeport?.AirBases?.ApplySetAction(areaId, baseId, actionKind);
							}

							// 念のため UI 全体更新も促す
							try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"[TryHandleAirCorps] Apply change error: {ex}");
						}
					});

					return true;
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[TryHandleAirCorps] Exception: {ex}");
			}

			return true; // endpoint にマッチしているためハンドル済み扱いにする
		}

		/// <summary>
		/// 基地航空隊 補給 (api_req_air_corps/supply)
		/// </summary>
		private bool TryHandleAirCorpsSupply(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_air_corps/supply")) return false;

			try
			{
				JToken root = null;
				try { root = JToken.Parse(normalized); } catch { root = null; }
				var data = root?["api_data"] ?? root;
				if (data == null) return true;

				// api_after_fuel / api_after_bauxite を取得（存在すれば絶対値）
				int? afterFuel = null;
				int? afterBauxite = null;
				try
				{
					var f = data["api_after_fuel"];
					if (f != null && f.Type == JTokenType.Integer) afterFuel = f.Value<int>();
				}
				catch { afterFuel = null; }

				try
				{
					var b = data["api_after_bauxite"];
					if (b != null && b.Type == JTokenType.Integer) afterBauxite = b.Value<int>();
				}
				catch { afterBauxite = null; }

				// 値がなければ特に処理する必要なし（既存ハンドラと同挙動）
				if (!afterFuel.HasValue && !afterBauxite.HasValue) return true;

				// UI スレッドで安全に反映
				RunOnUi(() =>
				{
					try
					{
						var materials = this.Homeport?.Materials;
						if (materials == null) return;

						// 現在値を取得（リフレクションで安全にアクセス）
						int curFuel = 0, curAmmo = 0, curSteel = 0, curBaux = 0;
						try
						{
							var ty = typeof(Materials);
							var pFuel = ty.GetProperty("Fuel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var pAmmo = ty.GetProperty("Ammunition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var pSteel = ty.GetProperty("Steel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var pBaux = ty.GetProperty("Bauxite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

							if (pFuel != null) curFuel = (int)pFuel.GetValue(materials);
							if (pAmmo != null) curAmmo = (int)pAmmo.GetValue(materials);
							if (pSteel != null) curSteel = (int)pSteel.GetValue(materials);
							if (pBaux != null) curBaux = (int)pBaux.GetValue(materials);
						}
						catch { }

						// 反映する新値を決定
						int newFuel = afterFuel ?? curFuel;
						int newBaux = afterBauxite ?? curBaux;

						// 可能なら個別プロパティにセット、それが無ければ private Update(int[]) を使って上書き
						try
						{
							var ty = typeof(Materials);
							var pFuel = ty.GetProperty("Fuel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var pBaux = ty.GetProperty("Bauxite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

							bool setFuel = false, setBaux = false;

							if (afterFuel.HasValue && pFuel != null)
							{
								pFuel.SetValue(materials, newFuel);
								setFuel = true;
							}
							if (afterBauxite.HasValue && pBaux != null)
							{
								pBaux.SetValue(materials, newBaux);
								setBaux = true;
							}

							// どちらかプロパティでセットできなかった場合は Update(int[]) で上書き
							if (!(setFuel && setBaux))
							{
								// 保持したい既存の ammo/steel を利用して配列を作る
								var mi = typeof(Materials).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int[]) }, null);
								if (mi != null)
								{
									var arr = new int[4];
									arr[0] = newFuel;
									arr[1] = curAmmo;
									arr[2] = curSteel;
									arr[3] = newBaux;
									mi.Invoke(materials, new object[] { arr });
								}
							}
						}
						catch { }

						// UI 全体更新を促す
						try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }
					}
					catch { }
				});
			}
			catch { }

			return true;
		}

		#endregion
	}
}
