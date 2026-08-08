using Grabacr07.KanColleWrapper.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 艦娘一覧・解体・補給系の API ハンドラーです。
	/// KanColleClient から呼び出され、Organization.Ships / Materials の更新を担当します。
	/// </summary>
	internal class ShipHandler
	{
		private readonly KanColleClient client;

		internal ShipHandler(KanColleClient client)
		{
			this.client = client;
		}

		/// <summary>
		/// 解体
		/// </summary>
		internal bool TryHandleDestroyShip(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_kousyou/destroyship")) return false;

			try
			{
				// api_material と api_unset_list をまとめて1回のパースで取得する
				int[] apiMat = null;
				bool hasUnsetList = false;
				try
				{
					var root = JToken.Parse(normalized);
					var data = root["api_data"] ?? root;

					var matTok = data?["api_material"];
					if (matTok != null && matTok.Type == JTokenType.Array)
					{
						apiMat = matTok.Select(t => (int?)t ?? 0).ToArray();
					}

					var unset = data?["api_unset_list"];
					if (unset != null && unset.HasValues) hasUnsetList = true;
				}
				catch (Exception)
				{
					apiMat = null;
					hasUnsetList = false;
				}

				// requestBody から解体対象艦 ID を取得
				var shipIds = new List<int>();
				if (!string.IsNullOrEmpty(requestBody))
				{
					try
					{
						var dict = HandlerHelper.ParseRequestBody(requestBody, StringComparer.OrdinalIgnoreCase);
						if (dict.TryGetValue("api_ship_id", out var ids) && !string.IsNullOrEmpty(ids))
						{
							foreach (var part in ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
							{
								if (int.TryParse(part, out var id)) shipIds.Add(id);
							}
						}
					}
					catch (Exception ex) { HandlerHelper.LogError("TryHandleDestroyShip", ex); }
				}

				// UI スレッドで反映
				HandlerHelper.RunOnUi(() =>
				{
					try
					{
						// 資源の反映
						// サーバーから返ってくる api_material は「現在の絶対値」を返すことがあるため、
						// 増分と誤認して現在値に加算すると二重加算になる。
						// ここでは api_material を受け取ったらそのまま Materials.Update(int[]) を呼んで上書きする。
						if (apiMat != null && apiMat.Length >= 4)
						{
							try
							{
								var materials = this.client.Homeport?.Materials;
								if (materials != null)
									materials.Update(apiMat);
							}
							catch (Exception ex) { HandlerHelper.LogError("TryHandleDestroyShip", ex); }
						}

						// 解体対象の艦を Organization から削除
						try
						{
							var org = this.client.Homeport?.Organization;
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
											try { this.client.Homeport?.Itemyard?.RemoveFromShip(ship); } catch { }
										}
										// いずれにせよ Ship 自体は削除
										try { org.Ships.Remove(ship); }
										catch (Exception)
										{
											// MemberTable.Remove(Ship) のオーバーロードがなければ id で削除
											try { org.Ships.Remove(shipId); } catch { }
										}
									}
									catch (Exception ex) { HandlerHelper.LogError("TryHandleDestroyShip", ex); }
								}

								// 艦娘一覧の変更通知
								try { org.RaiseShipsChanged(); } catch { org.NotifyUpdated(); }
							}
						}
						catch (Exception ex) { HandlerHelper.LogError("TryHandleDestroyShip", ex); }

						// 装備数・組織の UI 再評価
						try { this.client.Homeport?.Itemyard?.RaiseSlotItemsChanged(); } catch { }
						try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
					}
					catch (Exception ex) { HandlerHelper.LogError("TryHandleDestroyShip", ex); }
				});
			}
			catch (Exception ex) { HandlerHelper.LogError("TryHandleDestroyShip", ex); }

			return true;
		}

		/// <summary>
		/// 艦娘の情報更新
		/// </summary>
		internal bool TryHandleShipArray(string url, string normalized)
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
					HandlerHelper.RunOnUi(() =>
					{
						try
						{
							var org = this.client.Homeport?.Organization;
							if (org == null)
							{
								// 組織が未初期化なら既存のルートで反映
								try { this.client.Homeport.Organization.Update(ships); } catch { }
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
									catch (Exception)
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
								try { this.client.Homeport.Organization.Update(toCreate.ToArray()); } catch { }
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
							try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
						}
						catch (Exception)
						{
						}
					});
				}
			}
			catch (Exception)
			{
			}
			return true;
		}

		/// <summary>
		/// 補給処理
		/// </summary>
		internal bool TryHandleCharge(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_hokyu/charge")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_charge>(normalized, out var charge))
				{
					// charge.api_material : int[] (length=4) — Materials の private Update(int[]) を反射で呼び出して反映
					// charge.api_ship : kcsapi_charge_ship[] — 各艦の燃料/弾薬/onslot を更新し艦隊状態を再計算
					HandlerHelper.RunOnUi(() =>
					{
						try
						{
							// 資源反映
							var materials = this.client.Homeport?.Materials;
							if (materials != null && charge.api_material != null)
							{
								materials.Update(charge.api_material);
							}

							// Ships の補給反映
							if (charge.api_ship != null && charge.api_ship.Length > 0)
							{
								Fleet affectedFleet = null;
								var org = this.client.Homeport?.Organization;
								foreach (var s in charge.api_ship)
								{
									try
									{
										var ship = org?.Ships?[s.api_id];
										if (ship == null) continue;

										ship.Charge(s.api_fuel, s.api_bull, s.api_onslot);

										if (affectedFleet == null) affectedFleet = org.GetFleet(ship.Id);
									}
									catch (Exception)
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
							try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }

							// カウンタープラグイン用イベント発火
							try { this.client.RaiseSupplyCompleted(); } catch { }
						}
						catch (Exception)
						{
						}
					});
				}
			}
			catch (Exception ex) { HandlerHelper.LogError("TryHandleCharge", ex); }

			return true;
		}
	}
}
