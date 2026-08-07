using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 入渠関連の API ハンドラーです。
	/// KanColleClient から呼び出され、Repairyard および Materials の状態更新を担当します。
	/// </summary>
	internal class NyukyoHandler
	{
		private readonly KanColleClient client;

		internal NyukyoHandler(KanColleClient client)
		{
			this.client = client;
		}

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		/// <summary>
		/// レスポンス JSON から資源の増分（api_get_material 相当）を解析します。
		/// 解析できない場合は null を返します。
		/// </summary>
		private static int[] ParseAddMaterials(string normalized, string contextForLog)
		{
			try
			{
				JToken root = null;
				try { root = JToken.Parse(normalized); } catch { root = null; }
				var data = root?["api_data"] ?? root;
				var matTok = data?["api_get_material"] ?? data?["api_get_materials"] ?? data?["api_get"];
				if (matTok != null && matTok.Type == JTokenType.Array)
				{
					return matTok.Select(t => (int?)t ?? 0).ToArray();
				}
			}
			catch (Exception ex) { LogError(contextForLog, ex); }

			return null;
		}

		/// <summary>
		/// 入渠系1 ドック一覧
		/// </summary>
		internal bool TryHandleNdockList(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/ndock")) return false;

			try
			{
				// 型デシリアライズを優先
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_ndock[]>(normalized, out var ndocks))
				{
					RunOnUi(() =>
					{
						try
						{
							// Repairyard 側はそのまま更新
							this.client.Homeport?.Repairyard?.Update(ndocks);

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
									var materials = this.client.Homeport?.Materials;

									lock (this.client.NyukyoStateLock)
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
												if (this.client.appliedRepairNdock.Contains(ndockId)) this.client.appliedRepairNdock.Remove(ndockId);
												continue;
											}

											// 修復中で、まだ消費を適用していなければ集計してフラグを立てる
											if (state == 1 && !this.client.appliedRepairNdock.Contains(ndockId))
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

													this.client.appliedRepairNdock.Add(ndockId);
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
											materials.Update(newMat);
										}
									}
									catch (Exception ex) { LogError("TryHandleNdockList", ex); }
								}
							}
							catch (Exception)
							{
							}

							// UI 側の更新呼び出し（既存の挙動を維持）
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception)
						{
						}
					});

					return true;
				}

				// フォールバック: JSON をパースして api_data を探す（既存のフォールバックは維持）
				JToken rootTok;
				try { rootTok = JToken.Parse(normalized); } catch { rootTok = null; }
				var data2 = rootTok?["api_data"] ?? rootTok;
				if (data2 == null) return true;
				var ndockTok = data2.Type == JTokenType.Array ? data2 : data2["api_ndock"] ?? data2.SelectToken("api_ndock");
				if (ndockTok == null) return true;

				var parsed = ndockTok.ToObject<kcsapi_ndock[]>();
				if (parsed != null)
				{
					RunOnUi(() =>
					{
						try
						{
							this.client.Homeport?.Repairyard?.Update(parsed);
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandleNdockList", ex); }
					});
				}
			}
			catch (Exception)
			{
			}
			return true;
		}

		/// <summary>
		/// 入渠系2 入渠開始
		/// </summary>
		internal bool TryHandleNyukyoStart(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_nyukyo/start")) return false;

			// リクエスト body から api_ship_id / api_highspeed を参照する既存処理を継承
			var dict = HandlerHelper.ParseRequestBody(requestBody, StringComparer.OrdinalIgnoreCase);

			int shipId;
			if (!dict.ContainsKey("api_ship_id") || !int.TryParse(dict["api_ship_id"], out shipId)) return true;

			bool highspeedRequested = dict.ContainsKey("api_highspeed") && dict["api_highspeed"] == "1";

			// レスポンス側の資源情報を先に解析（増分扱い api_get_material 等）
			var addMaterials = ParseAddMaterials(normalized, "TryHandleNyukyoStart");

			RunOnUi(() =>
			{
				try
				{
					var ship = this.client.Homeport?.Organization?.Ships?[shipId];
					if (ship == null) return;

					// 既存の動作: 高速修復なら即時修復反映
					if (highspeedRequested)
					{
						try { ship.Repair(); } catch { }
					}

					// 資源の増分反映（api_get_material 相当を増分として扱う）
					HandlerHelper.ApplyMaterialDelta(this.client.Homeport?.Materials, addMaterials, "TryHandleNyukyoStart");

					// 高速修復材の即時減算（UI に即時反映）
					if (highspeedRequested)
					{
						try { this.client.Homeport?.Materials?.DecrementInstantRepairMaterials(); } catch { }
					}

					// 所属艦隊の状態を更新
					try { this.client.Homeport?.Organization?.GetFleet(ship.Id)?.State.Update(); } catch { }

					// 全体 UI の再評価
					try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
				}
				catch (Exception ex) { LogError("TryHandleNyukyoStart", ex); }
			});

			return true;
		}

		/// <summary>
		/// 入渠系3 高速修復
		/// </summary>
		internal bool TryHandleNyukyoSpeedChange(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_nyukyo/speedchange")) return false;

			// requestBody から api_ndock_id を取得する既存処理
			if (string.IsNullOrEmpty(requestBody)) return true;
			var dict = HandlerHelper.ParseRequestBody(requestBody, StringComparer.OrdinalIgnoreCase);
			if (!dict.ContainsKey("api_ndock_id")) return false;
			if (!int.TryParse(dict["api_ndock_id"], out var ndockId)) return false;

			// レスポンスの資源増分を解析
			var addMaterials = ParseAddMaterials(normalized, "TryHandleNyukyoSpeedChange");

			RunOnUi(() =>
			{
				try
				{
					var dock = this.client.Homeport?.Repairyard?.Docks?[ndockId];
					var ship = dock?.Ship;
					if (dock != null) dock.Finish();
					if (ship != null)
					{
						ship.Repair();
						this.client.Homeport?.Organization?.GetFleet(ship.Id)?.State.Update();
					}

					// 資源の増分反映（増分 api_get_material）
					HandlerHelper.ApplyMaterialDelta(this.client.Homeport?.Materials, addMaterials, "TryHandleNyukyoSpeedChange");

					// 高速修復材（speedchange は高速修復の結果なので -1 すると安全）
					try { this.client.Homeport?.Materials?.DecrementInstantRepairMaterials(); } catch { }

					// UI 更新
					try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
				}
				catch (Exception ex) { LogError("TryHandleNyukyoSpeedChange", ex); }
			});

			return true;
		}
	}
}
