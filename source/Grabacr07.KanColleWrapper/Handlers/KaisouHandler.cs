using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 艦娘装備関連（改装系）の API ハンドラーです。
	/// KanColleClient から呼び出され、Organization / Ship.RawData の装備状態更新を担当します。
	/// </summary>
	internal class KaisouHandler
	{
		private readonly KanColleClient client;

		internal KaisouHandler(KanColleClient client)
		{
			this.client = client;
		}

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		/// <summary>
		/// 近代化改修
		/// </summary>
		internal bool TryHandlePowerup(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/powerup")) return false;

			JToken root;
			try { root = JToken.Parse(normalized); }
			catch (Exception)
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
			catch (Exception ex) { slotTok = null; LogError("TryHandlePowerup", ex); }

			// requestBody から api_id_items を取り出して削除対象ID配列を用意する（CEF 経路で使う）
			// 注: powerup の api_id_items は「改修素材にした艦のID」の場合があるため、実行時に艦テーブルに存在するかで判定
			int[] apiIdItemsRaw = null;
			if (!string.IsNullOrEmpty(requestBody))
			{
				try
				{
					var dict = HandlerHelper.ParseRequestBody(requestBody, StringComparer.OrdinalIgnoreCase);

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
						catch (Exception ex) { apiIdItemsRaw = null; LogError("TryHandlePowerup", ex); }
					}
				}
				catch (Exception ex) { apiIdItemsRaw = null; LogError("TryHandlePowerup", ex); }
			}

			RunOnUi(() =>
			{
				try
				{
					var org = this.client.Homeport?.Organization;
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
											else this.client.Homeport.Organization.Update(new[] { raw });
											updatedShipIds.Add(raw.api_id);
										}
										catch (Exception ex) { LogError("TryHandlePowerup", ex); }
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
										else this.client.Homeport.Organization.Update(new[] { raw });
										updatedShipIds.Add(raw.api_id);
									}
									catch (Exception ex) { LogError("TryHandlePowerup", ex); }
								}
							}
						}
					}
					catch (Exception ex) { LogError("TryHandlePowerup", ex); }

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
									foreach (var d in decks) try { this.client.Homeport.Organization.Update(d); } catch { }
								}
							}
							else if (deckTok.Type == JTokenType.Object)
							{
								var deck = deckTok.ToObject<kcsapi_deck>();
								if (deck != null) try { this.client.Homeport.Organization.Update(deck); } catch { }
							}
						}
					}
					catch (Exception ex) { LogError("TryHandlePowerup", ex); }

					// 3) 装備アイテム更新：api_slot_item / api_slotitem があれば Itemyard を更新
					try
					{
						var iy = this.client.Homeport?.Itemyard;
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
										iy?.RaiseSlotItemsChanged();
									}
									catch (Exception ex) { LogError("TryHandlePowerup", ex); }
								}
							}
							catch (Exception ex) { LogError("TryHandlePowerup", ex); }
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
										iy?.RaiseSlotItemsChanged();
									}
									catch (Exception ex) { LogError("TryHandlePowerup", ex); }
								}
							}
							catch (Exception ex) { LogError("TryHandlePowerup", ex); }
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
										catch (Exception ex) { LogError("TryHandlePowerup", ex); }
									}
								}
								catch (Exception ex) { LogError("TryHandlePowerup", ex); }

								var isUnsetList = unsetListTok != null;

								foreach (var ship in shipsToRemove)
								{
									try
									{
										// 装備解除フラグがある場合は Itemyard から削除しない（装備が母港へ戻っただけのケース）
										if (!isUnsetList)
										{
											try { this.client.Homeport?.Itemyard?.RemoveFromShip(ship); } catch { }
										}
										else
										{
											// 装備解除時はスロットを再同期して UI の喪失を防ぐ
											try { ship.UpdateSlots(); } catch { }
										}

										// api_id_items が実際に艦の ID を表す場合は艦自体を Organization から削除する
										try { org.Ships.Remove(ship); }
										catch (Exception)
										{
											try { org.Ships.Remove(ship.Id); } catch { }
										}
									}
									catch (Exception ex) { LogError("TryHandlePowerup", ex); }
								}

								// Itemyard の再描画通知（装備解除時は必須、通常時も保険として呼ぶ）
								try
								{
									this.client.Homeport?.Itemyard?.RaiseSlotItemsChanged();
								}
								catch (Exception ex) { LogError("TryHandlePowerup", ex); }

								// 艦娘一覧の変更通知（既存実装に合わせる）
								try { org.RaiseShipsChanged(); }
								catch (Exception ex) { LogError("TryHandlePowerup/RaiseShipsChanged", ex); try { org.NotifyUpdated(); } catch (Exception ex2) { LogError("TryHandlePowerup/NotifyUpdated", ex2); } }
							}
							catch (Exception ex) { LogError("TryHandlePowerup", ex); }
						}
					}
					catch (Exception ex) { LogError("TryHandlePowerup", ex); }

					// 4) api_unset_list: 無条件削除は避け、装備一覧の再描画通知のみ行う（安全側）
					try
					{
						if (unsetListTok != null)
						{
							try
							{
								this.client.Homeport?.Itemyard?.RaiseSlotItemsChanged();
							}
							catch (Exception ex) { LogError("TryHandlePowerup", ex); }
						}
					}
					catch (Exception ex) { LogError("TryHandlePowerup", ex); }

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
					catch (Exception ex) { LogError("TryHandlePowerup", ex); }

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
					catch (Exception ex) { LogError("TryHandlePowerup", ex); }

					// 6) 組織・UI レベルの最終通知
					try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch (Exception ex) { LogError("TryHandlePowerup/NotifyUpdated", ex); }
					try { org?.RaiseShipsChanged(); }
					catch (Exception ex) { LogError("TryHandlePowerup/RaiseShipsChanged", ex); try { org?.NotifyUpdated(); } catch (Exception ex2) { LogError("TryHandlePowerup/NotifyUpdated2", ex2); } }
				}
				catch (Exception ex) { LogError("TryHandlePowerup", ex); }
			});

			return true;
		}

		/// <summary>
		/// 改装系1
		/// </summary>
		internal bool TryHandleShip3(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/ship3")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_ship3>(normalized, out var s3))
				{
					RunOnUi(() =>
					{
						try
						{
							var org = this.client.Homeport?.Organization;
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
											try { this.client.Homeport.Organization.Update(new[] { rawShip }); } catch { }
										}

										updatedShipIds.Add(rawShip.api_id);
									}
									catch (Exception ex) { LogError("TryHandleShip3", ex); }
								}
							}

							// デッキ情報は個別デッキごとに更新
							if (s3.api_deck_data != null)
							{
								foreach (var deck in s3.api_deck_data)
								{
									try { this.client.Homeport.Organization.Update(deck); } catch { }
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
							try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
						}
						catch (Exception ex) { LogError("TryHandleShip3", ex); }
					});
				}
			}
			catch (Exception ex) { LogError("TryHandleShip3", ex); }

			return true;
		}

		/// <summary>
		/// 改装系2 -装備スロット交換
		/// </summary>
		internal bool TryHandleSlotExchangeIndex(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/slot_exchange_index")) return false;

			// requestBody から ship id を先に探す（api_id, api_ship_id の両方を確認）
			int shipId = -1;
			if (!string.IsNullOrEmpty(requestBody))
			{
				var dict = HandlerHelper.ParseRequestBody(requestBody);
				if (dict.TryGetValue("api_id", out var idStr) && int.TryParse(idStr, out var idVal))
				{
					shipId = idVal;
				}
				else if (dict.TryGetValue("api_ship_id", out var shipIdStr) && int.TryParse(shipIdStr, out var shipIdVal))
				{
					shipId = shipIdVal;
				}
			}

			// --- JSON 側を解析して api_slot を柔軟に抽出 ---
			JToken root;
			try
			{
				root = JToken.Parse(normalized);
			}
			catch (Exception)
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
					var org = this.client.Homeport?.Organization;
					var ship = org?.Ships?[shipId];
					if (ship == null) return;

					lock (this.client.KaisouStateLock)
					{
						// RawData.api_slot を置き換えて UpdateSlots() を呼ぶ（Organization.ExchangeSlot と同等）
						ship.RawData.api_slot = apiSlot;
						ship.UpdateSlots();
					}

					// 所属艦隊を再計算・再通知
					HandlerHelper.RefreshFleetByShipId(org, ship.Id, calculateFirst: true, caller: "TryHandleSlotExchangeIndex");

					// 組織レベルの再通知で UI 再評価を促す
					try { org.NotifyUpdated(); } catch { }
				}
				catch (Exception)
				{
				}
			});

			return true;
		}

		/// <summary>
		/// 改装系3 他艦 装備スロット解除
		/// </summary>
		internal bool TryHandleSlotDeprive(string url, string normalized, string requestBody)
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
					var org = this.client.Homeport?.Organization;
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
										try { this.client.Homeport.Organization.Update(new[] { unsetShip }); } catch { }
									}
									affected.Add(unsetShip.api_id);
								}
							}
						}
						catch (Exception ex) { LogError("TryHandleSlotDeprive", ex); }

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
										try { this.client.Homeport.Organization.Update(new[] { setShip }); } catch { }
									}
									affected.Add(setShip.api_id);
								}
							}
						}
						catch (Exception ex) { LogError("TryHandleSlotDeprive", ex); }
					}

					// 重要: api_unset_list に含まれる装備を Itemyard から削除しない。
					// 削除してしまうと装備が移動された場合に UI 側で失われるため、
					// 削除処理は廃止し、代わりに Itemyard の再描画通知のみ行う。
					try
					{
						var unsetListTok = data["api_unset_list"] ?? data.SelectToken("api_unset_list");
						var iy = this.client.Homeport?.Itemyard;
						if (iy != null && unsetListTok != null)
						{
							// ここでは削除せず、UI の再描画だけを促す。
							// 将来的に「装備が Inventory に戻る／移動する」などの厳密な処理が必要なら
							// 受信 JSON の他フィールド（api_slotitem 等）を使って明示的に同期する。
							try
							{
								try { iy.RaiseSlotItemsChanged(); }
								catch (Exception ex) { LogError("TryHandleSlotDeprive", ex); }
							}
							catch (Exception ex) { LogError("TryHandleSlotDeprive", ex); }
						}
					}
					catch (Exception ex) { LogError("TryHandleSlotDeprive", ex); }

					// 影響を受ける艦隊を再計算・再通知
					foreach (var id in affected.Distinct())
					{
						HandlerHelper.RefreshFleetByShipId(org, id, calculateFirst: false, caller: "TryHandleSlotDeprive");
					}

					// 組織・艦娘一覧の再通知
					try { org.NotifyUpdated(); } catch { }
					try { org.RaiseShipsChanged(); } catch { }
				}
				catch (Exception)
				{
				}
			});

			return true;
		}

		/// <summary>
		/// 改装系4 拡張スロット開放
		/// </summary>
		internal bool TryHandleOpenExslot(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/open_exslot")) return false;

			// 成功レスポンスかどうかを確認
			bool isSuccess = false;
			try
			{
				var root = JToken.Parse(normalized);
				isSuccess = root["api_result"] != null && root["api_result"].Value<int>() == 1;
			}
			catch (Exception)
			{
				isSuccess = false;
			}

			if (!isSuccess) return true;

			// requestBody から api_ship_id を取得
			int shipId = -1;
			if (!string.IsNullOrEmpty(requestBody))
			{
				var dict = HandlerHelper.ParseRequestBody(requestBody);
				if (dict.TryGetValue("api_ship_id", out var idStr) && int.TryParse(idStr, out var idVal))
				{
					shipId = idVal;
				}
			}

			if (shipId <= 0) return true;

			// UI 更新
			RunOnUi(() =>
			{
				try
				{
					var org = this.client.Homeport?.Organization;
					var ship = org?.Ships?[shipId];
					if (ship == null) return;

					try
					{
						lock (this.client.KaisouStateLock)
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
						}

						HandlerHelper.RefreshFleetByShipId(org, ship.Id, calculateFirst: true, caller: "TryHandleOpenExslot");

						try { org.NotifyUpdated(); } catch { }
					}
					catch (Exception ex) { LogError("TryHandleOpenExslot", ex); }
				}
				catch (Exception ex) { LogError("TryHandleOpenExslot", ex); }
			});

			return true;
		}

		/// <summary>
		/// 改装系5 拡張スロットへの装備設定
		/// </summary>
		internal bool TryHandleSlotsetEx(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kaisou/slotset_ex")) return false;

			// requestBody から api_ship_id / api_slot_ex を試しに取得
			int shipId = -1;
			int slotExId = int.MinValue;
			if (!string.IsNullOrEmpty(requestBody))
			{
				var dict = HandlerHelper.ParseRequestBody(requestBody);
				if (dict.TryGetValue("api_ship_id", out var shipIdStr)) int.TryParse(shipIdStr, out shipId);
				if (dict.TryGetValue("api_slot_ex", out var slotExStr) || dict.TryGetValue("api_slot_ex_id", out slotExStr))
				{
					int.TryParse(slotExStr, out slotExId);
				}
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
			catch (Exception ex) { LogError("TryHandleSlotsetEx", ex); }

			if (shipId <= 0) return true;

			// UI 更新
			RunOnUi(() =>
			{
				try
				{
					var org = this.client.Homeport?.Organization;
					var ship = org?.Ships?[shipId];
					if (ship == null) return;

					try
					{
						lock (this.client.KaisouStateLock)
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
						}

						HandlerHelper.RefreshFleetByShipId(org, ship.Id, calculateFirst: false, caller: "TryHandleSlotsetEx");

						try { org.NotifyUpdated(); } catch { }
					}
					catch (Exception ex) { LogError("TryHandleSlotsetEx", ex); }
				}
				catch (Exception ex) { LogError("TryHandleSlotsetEx", ex); }
			});

			return true;
		}
	}
}
