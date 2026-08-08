using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 母港・遠征・イベント難易度選択に関する API を処理するハンドラーです。
	/// </summary>
	internal class PortHandler
	{
		private readonly KanColleClient client;

		public PortHandler(KanColleClient client)
		{
			this.client = client;
		}

		/// <summary>
		/// イベントマップ難易度選択 (api_req_map/select_eventmap_rank)
		/// </summary>
		public bool TryHandleSelectEventmapRank(string url, string requestBody, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_map/select_eventmap_rank")) return false;

			this.client.Proxy.PublishSession(
				"/kcsapi/api_req_map/select_eventmap_rank",
				normalized,
				HandlerHelper.ParseRequestBody(requestBody));

			return true;
		}

		/// <summary>
		/// 母港 (api_port/port)
		/// </summary>
		public bool TryHandlePort(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_port/port")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_port>(normalized, out var port))
				{
					// JSON 側も柔軟にパースして api_slot_item 等を探すためのトークンを準備
					JToken root = null;
					JToken dataTok = null;
					try { root = JToken.Parse(normalized); dataTok = root["api_data"] ?? root; } catch { root = null; dataTok = null; }

					HandlerHelper.RunOnUi(() =>
					{
						try
						{
							// Homeport が未初期化の場合は安全に作成する
							try
							{
								this.client.EnsureHomeport();
							}
							catch (Exception)
							{
								// 初期化に失敗したら以降の処理をスキップ
								return;
							}
							if (this.client.Homeport == null) return;

							if (port.api_basic != null) this.client.Homeport.UpdateAdmiral(port.api_basic);
							if (port.api_ship != null) this.client.Homeport.Organization.Update(port.api_ship);
							if (port.api_ndock != null) this.client.Homeport.Repairyard.Update(port.api_ndock);
							if (port.api_deck_port != null) this.client.Homeport.Organization.Update(port.api_deck_port);

							this.client.Homeport.Organization.Combined = port.api_combined_flag != 0;

							if (port.api_material != null) this.client.Homeport.Materials.Update(port.api_material);

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
											this.client.Homeport.Itemyard.Update(slotItems);
											try { this.client.Homeport?.Itemyard?.RaiseSlotItemsChanged(); } catch { }
										}
									}
									catch (Exception ex) { HandlerHelper.LogError("TryHandlePort", ex); }
								}
							}
							catch (Exception ex) { HandlerHelper.LogError("TryHandlePort", ex); }

							// UI バインディングが更新されないケースに備え、明示的に通知を出す
							try
							{
								this.client.Homeport?.Organization?.NotifyUpdated();
							}
							catch (Exception)
							{
							}

							// 各艦隊を明示的に再計算・再通知して UI を確実に更新
							try
							{
								var org = this.client.Homeport?.Organization;
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
										catch (Exception ex) { HandlerHelper.LogError("TryHandlePort", ex); }
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

						// 出撃していたデッキを復帰させる処理とグローバル出撃フラグ更新
						try
						{
							var org = this.client.Homeport?.Organization;
							if (org != null)
							{
								// lock 内で sortieDeckIds を読み取り、returning をローカルにコピー
								int[] returning;
								lock (this.client.BattleStateLock)
									returning = this.client.sortieDeckIds.Intersect(org.Fleets.Keys).ToArray();

								if (returning.Length > 0)
								{
									try
									{
										// 脱出フラグのクリアや全艦 Situation のリセットも含めて一括処理
										// (Homing は lock 外で実行してデッドロックを避ける)
										org.Homing();
									}
									catch (Exception ex) { HandlerHelper.LogError("TryHandlePort", ex); }

									lock (this.client.BattleStateLock)
									{
										foreach (var returningDeckId in returning)
											this.client.sortieDeckIds.Remove(returningDeckId);
									}
								}

								bool isInSortie;
								lock (this.client.BattleStateLock)
									isInSortie = this.client.sortieDeckIds.Count > 0;
								this.client.IsInSortie = isInSortie;
							}
						}
						catch (Exception)
						{
						}

						// SortieInfo をリセット
						try
						{
							this.client.SortieInfo.Reset();
							// 母港に戻ったら pending は破棄
							lock (this.client.BattleStateLock)
							{
								this.client.hasPendingAirResult = false;
								this.client.pendingAirResult = AirSuperiority.None;
							}
						}
						catch (Exception ex) { HandlerHelper.LogError("TryHandlePort", ex); }
					});
				}
				else
				{
					// 解析失敗でも true を返してハンドリング済みとする（既存ハンドラと同様の挙動）
				}
			}
			catch (Exception)
			{
			}

			this.client.Proxy.PublishSession("/kcsapi/api_port/port", normalized);
			return true;
		}

		/// <summary>
		/// 遠征結果 (api_req_mission/result)
		/// </summary>
		public bool TryHandleMissionResult(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_req_mission/result")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_mission_result>(normalized, out var result))
				{
					// api_clear_result: 0=失敗, 1=成功, 2=大成功
					if (result.api_clear_result == 1 || result.api_clear_result == 2)
					{
						HandlerHelper.RunOnUi(() =>
						{
							try { this.client.RaiseMissionSucceeded(); } catch { }
						});
					}
				}
			}
			catch (Exception ex) { HandlerHelper.LogError("TryHandleMissionResult", ex); }

			return true;
		}
	}
}
