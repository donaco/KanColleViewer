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

		// 直近に処理した api_req_hensei/change の deckId を一時保持する（requestBody が来ない時用）
		private int lastChangeDeckId = -1;

		// 直近に処理した建造ドック ID を保持（createship の requestBody が届く時用）
		private int lastCreateKdockId = -1;

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

				// （ProcessCaptured 内のハンドラ呼び出し群を以下に置換）
				// 先に map/start を判定して出撃フラグや該当艦隊の Sortie を行う（CEF 経路でのフォールバック）
				if (TryHandleMapStart(url, requestBody)) return;

				// 小さな処理に分割して判定（早期 return ）
				if (TryHandlePort(url, normalized)) return;

				// 任務完了や個別素材/消費アイテムの更新
				if (TryHandleClearItemGet(url, normalized)) return;
				if (TryHandleDestroyItem2(url, normalized, requestBody)) return;
				if (TryHandleDestroyShip(url, normalized, requestBody)) return;
				if (TryHandleMaterial(url, normalized)) return;
				if (TryHandleUseItem(url, normalized)) return;

				if (TryHandleQuestList(url, normalized)) return;
				if (TryHandleShipArray(url, normalized)) return;
				if (TryHandleSlotExchangeIndex(url, normalized, requestBody)) return;
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

				// 建造系
				if (TryHandleCreateShip(url, normalized, requestBody)) return;
				if (TryHandleKdock(url, normalized)) return;
				if (TryHandleGetShip(url, normalized)) return;

				if (TryHandleBattleResult(url, normalized)) return;

				// 入渠系
				if (TryHandleNyukyoSpeedChange(url, requestBody)) return;
				if (TryHandleNyukyoStart(url, requestBody)) return;
				if (TryHandleNdockList(url, normalized)) return;

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
									catch
									{
									}
									this.sortieDeckIds.Remove(returningDeckId);
								}
							}

							this.IsInSortie = this.sortieDeckIds.Count > 0;
						}
						catch
						{
						}
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
		/// /kcsapi/api_get_member/useitem を CEF 経路で受け取ったときに Itemyard.Update(kcsapi_useitem[]) を呼ぶ。
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
		/// 解体（/api_req_kousyou/destroyship）を CEF 経路で受け取った場合に反映する。
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
		/// 編成系1
		/// </summary>
		private bool TryHandleDecks(string url, string normalized, string requestBody)
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
		/// 建造系1 建造ドック ID をキャッシュ
		/// </summary>
		private bool TryHandleCreateShip(string url, string normalized, string requestBody)
		{
			if (!(url.Contains("/kcsapi/api_req_kousyou/createship") || url.Contains("/kcsapi/api_req_kousyou/createship_speedchange")))
				return false;

			if (string.IsNullOrEmpty(requestBody)) return true;

			try
			{
				var pairs = requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var p in pairs)
				{
					var kv = p.Split(new[] { '=' }, 2);
					if (kv.Length != 2) continue;
					var key = kv[0];
					var val = Uri.UnescapeDataString(kv[1]);
					// 代表的なパラメータ名をチェック
					if (key == "api_kdock_id" || key == "api_kdock" || key == "api_kdockno" || key == "api_kdock_id")
					{
						if (int.TryParse(val, out var id))
						{
							this.lastCreateKdockId = id;
							break;
						}
					}
				}
			}
			catch
			{
				// swallow
			}

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
			if (data == null) return true;

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
								try
								{
									var kdocks = kdockTok.ToObject<kcsapi_kdock[]>();
									if (kdocks != null) this.Homeport?.Dockyard?.Update(kdocks);
								}
								catch
								{
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
												// AddFromDock は差分追加を正しく処理する想定
												this.Homeport?.Itemyard?.AddFromDock(kd);
											}
											catch
											{
											}
										}
										kdockProcessed = true;
									}
								}
								catch
								{
									// パース失敗なら kdockProcessed は false のまま（フォールバック処理へ）
								}
							}
							catch
							{
								// swallow
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
									try { this.Homeport.Organization.Update(new[] { ship }); } catch { }

									// 生成艦の属する艦隊だけ再計算
									try
									{
										var f = org?.GetFleet(ship.api_id);
										if (f != null) { f.State.Update(); f.State.Calculate(); f.RaiseShipsUpdated(); }
									}
									catch { }
								}
							}
							catch { }
						}

						// --- 装備アイテム更新 ---
						// kdockProcessed が true の場合は AddFromDock() で装備追加が処理済みのはずなので
						// slotTok による置換は行わない（丸ごと置換で既存装備が消える問題を防止）。
						if (!kdockProcessed && slotTok != null)
						{
							try
							{
								var newItems = slotTok.ToObject<kcsapi_slotitem[]>();
								if (newItems != null && newItems.Length > 0)
								{
									// 可能であれば既存アイテムとマージする試み（失敗時は単純更新にフォールバック）
									try
									{
										var itemyard = this.Homeport?.Itemyard;
										if (itemyard != null)
										{
											// 既存アイテムを MemberTable から抽出して kcsapi_slotitem[] に変換するのは型依存で複雑なため、
											// ここでは安全側として「新規配列で Update」するが、kdockProcessed が true の場合はこの道は通らない。
											this.Homeport?.Itemyard?.Update(newItems);
										}
										else
										{
											this.Homeport?.Itemyard?.Update(newItems);
										}
									}
									catch
									{
										// フォールバック
										try { this.Homeport?.Itemyard?.Update(newItems); } catch { }
									}
								}
							}
							catch
							{
								// swallow
							}
						}

						// --- 組織レベルで通知して UI を更新 ---
						try { this.Homeport?.Organization?.NotifyUpdated(); } catch { }

						// 全フリート再計算（保険）
						try
						{
							if (org != null)
							{
								foreach (var f2 in org.Fleets.Values)
								{
									try { f2.State.Calculate(); f2.State.Update(); f2.RaiseShipsUpdated(); } catch { }
								}
							}
						}
						catch { }
					}
					catch { }
				});
			}
			catch
			{
			}

			// 取得処理は完了したので true を返す
			return true;
		}

		/// <summary>
		/// 戦闘結果
		/// </summary>
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
					foreach (var f in org.Fleets.Values)
					{
						try
						{
							var expeditionState = f.Expedition != null ? f.Expedition.IsInExecution.ToString() : "null";
							var situation = f.State != null ? f.State.Situation.ToString() : "(null)";
						}
						catch { }
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

					// 追加: 組織レベルでも明示通知
					try
					{
						this.Homeport?.Organization?.NotifyUpdated();
					}
					catch
					{
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
								catch
								{
								}
							}, System.Windows.Threading.DispatcherPriority.Background);
						}
					}
					catch
					{
					}



					// --- 追加: 更新後の各艦隊状態を出力（診断用） ---
					foreach (var f in org.Fleets.Values)
					{
						try
						{
							var expeditionState = f.Expedition != null ? f.Expedition.IsInExecution.ToString() : "null";
							var situation = f.State != null ? f.State.Situation.ToString() : "(null)";
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
					RunOnUi(() =>
					{
						try
						{
							this.Homeport?.Repairyard?.Update(ndocks);
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
