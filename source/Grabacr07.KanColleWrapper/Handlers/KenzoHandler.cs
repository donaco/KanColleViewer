using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 建造関連の API ハンドラーです。
	/// KanColleClient から呼び出され、Dockyard / Itemyard / Materials の状態更新を担当します。
	/// 建造用の共有状態はすべてこのクラス内で完結し、KenzoStateLock で保護します。
	/// </summary>
	internal class KenzoHandler
	{
		private readonly KanColleClient client;

		/// <summary>建造関連の共有状態を保護するロックオブジェクト。</summary>
		private readonly object KenzoStateLock = new object();

		/// <summary>建造でキャッシュする消費資源（kdockId をキーとする）。</summary>
		private readonly Dictionary<int, int[]> pendingCreateMaterials = new Dictionary<int, int[]>();

		/// <summary>高速建造材を即時減算済みの kdockId を保持する（二重減算防止用）。</summary>
		private readonly HashSet<int> appliedBuildKdock = new HashSet<int>();

		/// <summary>直近に処理した建造ドック ID を保持（createship の requestBody が届く時用）。</summary>
		private int lastCreateKdockId = -1;

		internal KenzoHandler(KanColleClient client)
		{
			this.client = client;
		}

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		/// <summary>
		/// 建造系1 建造ドック ID をキャッシュ
		/// </summary>
		internal bool TryHandleCreateShip(string url, string normalized, string requestBody)
		{
			if (!(url.Contains("/kcsapi/api_req_kousyou/createship") || url.Contains("/kcsapi/api_req_kousyou/createship_speedchange")))
				return false;

			if (string.IsNullOrEmpty(requestBody)) return true;

			// dict と keyId を外側スコープで宣言して後続から参照できるようにする
			IReadOnlyDictionary<string, string> dict = null;
			int keyId = -1;
			int[] items = null;

			try
			{
				int kdockIdFound = -1;

				// requestBody を dict にパース（ここで全てのキーを取得）
				dict = HandlerHelper.ParseRequestBody(requestBody, StringComparer.OrdinalIgnoreCase);

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
					lock (this.KenzoStateLock)
					{
						this.pendingCreateMaterials[keyId] = items;
					}
				}
			}
			catch (Exception)
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
						lock (this.KenzoStateLock)
						{
							if (!this.appliedBuildKdock.Contains(kdId)) this.appliedBuildKdock.Add(kdId);
						}

						// api_item1 が 1000 以上の場合は高速建造材を 10 消費、それ以外は 1 消費
						int buildCost = (items != null && items.Length >= 1 && items[0] >= 1000) ? 10 : 1;

						// UI スレッドで即時に InstantBuildMaterials を減算
						RunOnUi(() =>
						{
							try { this.client.Homeport?.Materials?.DecrementInstantBuildMaterials(buildCost); }
							catch (Exception) { }
						});
					}
				}
			}
			catch (Exception ex) { LogError("TryHandleCreateShip", ex); }

			// createship のレスポンスに資源情報が含まれていれば既存ロジックで反映（残す）
			try
			{
				if (!string.IsNullOrEmpty(normalized))
				{
					JToken root;
					try { root = JToken.Parse(normalized); }
					catch (Exception ex) { root = null; LogError("TryHandleCreateShip", ex); }

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
										var materials = this.client.Homeport?.Materials;
										if (materials != null && matArr != null && matArr.Length >= 4)
											materials.Update(matArr);
									}
									catch (Exception ex) { LogError("TryHandleCreateShip", ex); }
								});
							}
							catch (Exception ex) { LogError("TryHandleCreateShip", ex); }
						}
					}
				}
			}
			catch (Exception ex) { LogError("TryHandleCreateShip", ex); }

			return true;
		}

		/// <summary>
		/// 建造系2 ドック一覧
		/// </summary>
		internal bool TryHandleKdock(string url, string normalized)
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
							this.client.Homeport?.Dockyard?.Update(kdocks);
							// 各 kdock に対して pending があれば適用
							try
							{
								foreach (var rawK in kdocks)
								{
									try { this.ApplyPendingCreateMaterialsForKdock(rawK.api_id, rawK.api_state); } catch { }
								}
							}
							catch (Exception ex) { LogError("TryHandleKdock", ex); }

							// Dock の変化は UI に影響するため全体再通知・艦隊再計算
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandleKdock", ex); }
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
							this.client.Homeport?.Dockyard?.Update(parsed);
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandleKdock", ex); }
					});
				}
			}
			catch (Exception)
			{
			}
			return true;
		}

		/// <summary>
		/// 建造系3 艦娘の入手処理
		/// </summary>
		internal bool TryHandleGetShip(string url, string normalized)
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
						var org = this.client.Homeport?.Organization;

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
										this.client.Homeport?.Dockyard?.Update(kdocks);
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
												this.client.Homeport?.Itemyard?.AddFromDock(kd);

												// kdock_getship に api_state が無い場合があるため、kdock 配列のマップを参照して state を取得する
												int? state = null;
												try
												{
													if (kdockStateMap != null && kdockStateMap.TryGetValue(kd.api_id, out var s)) state = s;
												}
												catch (Exception ex) { state = null; LogError("TryHandleGetShip", ex); }

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
														var iy = this.client.Homeport?.Itemyard;
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
																iy?.RaiseSlotItemsChanged();
															}
															catch (Exception)
															{
															}
														}
														else
														{
															this.client.Homeport?.Itemyard?.Update(newItems);
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
											this.client.Homeport?.Itemyard?.RaiseSlotItemsChanged();
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
										this.client.Homeport.Organization.Update(new[] { ship });
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
												org?.Ships.Add(new Ship(this.client.Homeport, ship));
												try { org?.RaiseShipsChanged(); }
												catch (Exception) { try { org?.NotifyUpdated(); } catch { } }
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
										var itemyard = this.client.Homeport?.Itemyard;
										if (itemyard != null)
										{
											itemyard.Update(newItems);
											try
											{
												itemyard.RaiseSlotItemsChanged();
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
							}
							catch (Exception)
							{
							}
						}

						// --- 組織レベルで通知して UI を更新 / 全フリート再計算（保険） ---
						HandlerHelper.RefreshAllFleets(this.client.Homeport);
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
		/// <remarks>
		/// このメソッドは UI スレッド上（RunOnUi のコールバック内部）から呼ばれる前提です。
		/// Materials を直接更新するため、UI スレッド以外から呼び出さないでください。
		/// </remarks>
		private void ApplyPendingCreateMaterialsForKdock(int kdockId, int? api_state = null)
		{
			try
			{
				int[] req = null;
				lock (this.KenzoStateLock)
				{
					if (!this.pendingCreateMaterials.TryGetValue(kdockId, out req)) return;
					this.pendingCreateMaterials.Remove(kdockId);
				}

				var materials = this.client.Homeport?.Materials;
				if (materials == null || req == null) return;

				// 1) 燃料/弾薬/鋼材/ボーキ を差し引く（負にならないようガード）
				HandlerHelper.ApplyMaterialConsumption(materials, req, "ApplyPendingCreateMaterialsForKdock");

				// 2) api_item5 は開発資材 (DevelopmentMaterials) として減算する
				if (req.Length > 4)
				{
					materials.DecrementDevelopmentMaterials(req[4]);
				}

				// 3) api_state による InstantBuildMaterials の減算（api_state == 3 の場合は使用とみなして -1）
				//    ただし、createship_speedchange 等ですでに即時減算済みなら二重減算しない
				bool alreadyApplied = false;
				lock (this.KenzoStateLock)
				{
					if (this.appliedBuildKdock.Contains(kdockId))
					{
						alreadyApplied = true;
						this.appliedBuildKdock.Remove(kdockId);
					}
				}
				if (!alreadyApplied && api_state.HasValue && api_state.Value == 3)
				{
					materials.DecrementInstantBuildMaterials();
				}
			}
			catch (Exception)
			{
			}
		}
	}
}
