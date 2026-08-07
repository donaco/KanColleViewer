using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json.Linq;
using System;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 基地航空隊関連の API ハンドラーです。
	/// KanColleClient から呼び出され、AirBases の状態更新を担当します。
	/// </summary>
	internal class AirBaseHandler
	{
		private readonly KanColleClient client;

		internal AirBaseHandler(KanColleClient client)
		{
			this.client = client;
		}

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		/// <summary>
		/// 基地航空隊のスロット変更
		/// </summary>
		internal bool TryHandleSetPlane(string url, string normalized, string requestBody)
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
					catch (Exception ex) { LogError("TryHandleSetPlane", ex); }
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
				catch (Exception ex) { planeInfo = null; LogError("TryHandleSetPlane", ex); }

				try
				{
					var distanceTok = data["api_distance"];
					if (distanceTok != null && distanceTok.Type == JTokenType.Object)
					{
						distance = distanceTok.ToObject<ApiDistance>();
					}
				}
				catch (Exception ex) { distance = null; LogError("TryHandleSetPlane", ex); }

				if (planeInfo == null && distance == null)
				{
					return true;
				}

				// UI スレッドで航空隊情報を一時更新
				RunOnUi(() =>
				{
					lock (this.client.AirBaseStateLock)
					{
						try
						{
							var airBases = this.client.Homeport?.AirBases;

							if (airBases == null) return;

							var airBase = airBases.AreaGroup?[areaId];

							if (airBase != null)
							{
								airBase.UpdateFromSetPlane(planeInfo, distance, baseId);
							}
						}
						catch (Exception ex)
						{
							LogError("TryHandleSetPlane", ex);
						}
					}
				});
			}
			catch (Exception)
			{
			}

			return true;
		}

		/// <summary>
		/// 基地航空隊 名称・出撃状態の変更
		/// </summary>
		internal bool TryHandleAirCorpsChangeOrSet(string url, string normalized, string requestBody)
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
									lock (this.client.AirBaseStateLock)
									{
										try { this.client.Homeport?.AirBases?.Update(ab, abi); }
										catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TryHandleAirCorps] Update fallback error: {ex}"); }
									}
								});

								// 既にレスポンスで更新したので終了しても良い
								return true;
							}
						}
					}
				}
				catch (Exception ex) { LogError("TryHandleAirCorpsChangeOrSet", ex); }

				// requestBody から得られた情報で個別更新を試みる
				if (areaId > 0 && baseId > 0)
				{
					RunOnUi(() =>
					{
						lock (this.client.AirBaseStateLock)
						{
							try
							{
								if (url.IndexOf("/change_name", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									this.client.Homeport?.AirBases?.ApplyChangeName(areaId, baseId, name);
								}
								else if (url.IndexOf("/set_action", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									this.client.Homeport?.AirBases?.ApplySetAction(areaId, baseId, actionKind);
								}

								// 念のため UI 全体更新も促す
								try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
							}
							catch (Exception ex)
							{
								System.Diagnostics.Debug.WriteLine($"[TryHandleAirCorps] Apply change error: {ex}");
							}
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
		internal bool TryHandleAirCorpsSupply(string url, string normalized)
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
				catch (Exception ex) { afterFuel = null; LogError("TryHandleAirCorpsSupply", ex); }

				try
				{
					var b = data["api_after_bauxite"];
					if (b != null && b.Type == JTokenType.Integer) afterBauxite = b.Value<int>();
				}
				catch (Exception ex) { afterBauxite = null; LogError("TryHandleAirCorpsSupply", ex); }

				// api_plane_info を取得
				kcsapi_plane_info[] planeInfo = null;
				try
				{
					var planeTok = data["api_plane_info"];
					if (planeTok != null && planeTok.Type == JTokenType.Array)
					{
						planeInfo = planeTok.ToObject<kcsapi_plane_info[]>();
					}
				}
				catch (Exception ex) { planeInfo = null; LogError("TryHandleAirCorpsSupply", ex); }

				// api_distance を取得
				ApiDistance distance = null;
				try
				{
					var distanceTok = data["api_distance"];
					if (distanceTok != null && distanceTok.Type == JTokenType.Object)
					{
						distance = distanceTok.ToObject<ApiDistance>();
					}
				}
				catch (Exception ex) { distance = null; LogError("TryHandleAirCorpsSupply", ex); }

				// UI スレッドで安全に反映
				RunOnUi(() =>
				{
					lock (this.client.AirBaseStateLock)
					{
						try
						{
							var materials = this.client.Homeport?.Materials;

							// 資源の更新
							if (materials != null && (afterFuel.HasValue || afterBauxite.HasValue))
							{
								try
								{
									int newFuel = afterFuel ?? materials.Fuel;
									int newBaux = afterBauxite ?? materials.Bauxite;
									materials.SetFuelAndBauxite(newFuel, newBaux);
								}
								catch (Exception ex) { LogError("TryHandleAirCorpsSupply", ex); }
							}

							// 航空隊の搭載数を更新（api_plane_info がある場合）
							if (planeInfo != null && planeInfo.Length > 0)
							{
								try
								{
									var airBases = this.client.Homeport?.AirBases;
									if (airBases != null)
									{
										// 全海域の全基地に対して搭載数を更新
										foreach (var kvp in airBases.AreaGroup)
										{
											try
											{
												kvp.Value?.UpdateFromSupply(planeInfo, distance);
											}
											catch (Exception ex) { LogError("TryHandleAirCorpsSupply", ex); }
										}
									}
								}
								catch (Exception ex) { LogError("TryHandleAirCorpsSupply", ex); }
							}

							// UI 全体更新を促す
							try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
						}
						catch (Exception ex) { LogError("TryHandleAirCorpsSupply", ex); }
					}
				});
			}
			catch (Exception ex) { LogError("TryHandleAirCorpsSupply", ex); }

			return true;
		}
	}
}
