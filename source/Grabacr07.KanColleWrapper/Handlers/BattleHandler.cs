using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Windows;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 出撃・戦闘関連の API ハンドラーです。
	/// KanColleClient から呼び出され、戦闘状態の更新を担当します。
	/// </summary>
	internal class BattleHandler
	{
		private readonly KanColleClient client;

		internal BattleHandler(KanColleClient client)
		{
			this.client = client;
		}

		// ── ショートカット ──────────────────────────────

		private KanColleClient Client => this.client;

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		private static bool TryParseAirSuperiority(string normalized, out AirSuperiority airResult, string ctx)
			=> HandlerHelper.TryParseAirSuperiority(normalized, out airResult, ctx);

		private static bool TryParseAirSuperiorityFromApiData(JToken data, out AirSuperiority airResult)
			=> HandlerHelper.TryParseAirSuperiorityFromApiData(data, out airResult);

		// ── 出撃開始 ──────────────────────────────────

		/// <summary>出撃開始 (api_req_map/start)</summary>
		internal bool TryHandleMapStart(string url, string requestBody, string normalized)
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
					catch (Exception)
					{
					}
				}

				if (deckId > 0)
				{
					try
					{
						var org = Client.Homeport?.Organization;
						if (org != null && org.Fleets.ContainsKey(deckId))
						{
							org.Fleets[deckId].Sortie();
							lock (Client.BattleStateLock)
								Client.sortieDeckIds.Add(deckId);

							if (deckId == 1)
							{
								bool isCombined = false;
								try { isCombined = org.Combined; } catch { isCombined = false; }

								if (isCombined && org.Fleets.ContainsKey(2))
								{
									org.Fleets[2].Sortie();
									lock (Client.BattleStateLock)
										Client.sortieDeckIds.Add(2);
								}
							}
						}
					}
					catch (Exception)
					{
					}
				}

				// SortieInfo の更新（出撃開始）
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

							lock (Client.BattleStateLock)
							{
								Client.cachedCellNo = cellNo;
								if (mapAreaId > 0 && mapInfoNo > 0)
									Client.cachedMapId = mapAreaId * 10 + mapInfoNo;
							}

							if (mapAreaId > 0 && mapInfoNo > 0)
							{
								RunOnUi(() =>
								{
									try
									{
										var showOnArrival = Client.Settings?.ShowCellOnArrival ?? false;
										if (showOnArrival && cellNo > 0)
										{
											Client.SortieInfo.Start(mapAreaId, mapInfoNo, 0);
											Client.SortieInfo.Next(cellNo);
										}
										else
										{
											Client.SortieInfo.Start(mapAreaId, mapInfoNo, 0);
										}
										lock (Client.BattleStateLock)
										{
											Client.hasPendingAirResult = false;
											Client.pendingAirResult = AirSuperiority.None;
										}
									}
									catch (Exception ex) { LogError("BattleHandler.TryHandleMapStart", ex); }
								});
							}
						}
					}
				}
				catch (Exception ex) { LogError("BattleHandler.TryHandleMapStart", ex); }

				RunOnUi(() =>
				{
					try
					{
						Client.IsInSortie = true;
					}
					catch (Exception)
					{
					}
				});
			}
			catch (Exception)
			{
			}
			Client.Proxy.PublishSession("/kcsapi/api_req_map/start", normalized, HandlerHelper.ParseRequestBody(requestBody));
			return true;
		}

		// ── 次海域進撃 ──────────────────────────────────

		/// <summary>次の海域へ進撃 (api_req_map/next)</summary>
		internal bool TryHandleMapNext(string url, string normalized)
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

						int mapAreaId = data["api_maparea_id"]?.Value<int>() ?? 0;
						int mapInfoNo = data["api_mapinfo_no"]?.Value<int>() ?? 0;
						lock (Client.BattleStateLock)
						{
							if (mapAreaId > 0 && mapInfoNo > 0)
								Client.cachedMapId = mapAreaId * 10 + mapInfoNo;
						}

						if (cellNo > 0)
						{
							lock (Client.BattleStateLock)
								Client.cachedCellNo = cellNo;

							var showOnArrival = Client.Settings?.ShowCellOnArrival ?? false;
							if (showOnArrival)
							{
								RunOnUi(() =>
								{
									try
									{
										Client.SortieInfo.Next(cellNo);
									}
									catch (Exception ex) { LogError("BattleHandler.TryHandleMapNext", ex); }
									lock (Client.BattleStateLock)
									{
										Client.hasPendingAirResult = false;
										Client.pendingAirResult = AirSuperiority.None;
									}
								});
							}
							else
							{
								lock (Client.BattleStateLock)
								{
									Client.hasPendingAirResult = false;
									Client.pendingAirResult = AirSuperiority.None;
								}
							}
						}

						// 防空戦 (api_destruction_battle) の制空結果を解析
						try
						{
							var destructionBattle = data["api_destruction_battle"];
							if (destructionBattle != null)
							{
								var stage1 = destructionBattle.SelectToken("api_air_base_attack.api_stage1");
								if (stage1 != null)
								{
									var dispSeiku = stage1["api_disp_seiku"];
									if (dispSeiku != null)
									{
										int val;
										if (int.TryParse(dispSeiku.ToString(), out val) && val >= 0 && val <= 4)
										{
											var airResult = (AirSuperiority)val;
											RunOnUi(() =>
											{
												try
												{
													Client.SortieInfo.SetDestructionAirResult(airResult);
												}
												catch (Exception ex) { LogError("BattleHandler.TryHandleMapNext", ex); }
											});
										}
									}
								}
							}
						}
						catch (Exception ex) { LogError("BattleHandler.TryHandleMapNext", ex); }
					}
				}
			}
			catch (Exception ex) { LogError("BattleHandler.TryHandleMapNext", ex); }

			Client.Proxy.PublishSession("/kcsapi/api_req_map/next", normalized);
			return true;
		}

		// ── 戦闘開始 ──────────────────────────────────

		/// <summary>戦闘開始（各種 battle API）</summary>
		internal bool TryHandleBattle(string url, string normalized)
		{
			if (!(url.Contains("/kcsapi/api_req_sortie/battle")
				|| url.Contains("/kcsapi/api_req_sortie/airbattle")
				|| url.Contains("/kcsapi/api_req_sortie/ld_airbattle")
				|| url.Contains("/kcsapi/api_req_battle_midnight")
				|| url.Contains("/kcsapi/api_req_combined_battle/")))
				return false;

			if (url.Contains("battleresult")) return false;
			if (url.Contains("goback_port")) return false;

			bool isLdAirbattle =
				url.Contains("/kcsapi/api_req_sortie/ld_airbattle")
				|| url.Contains("/kcsapi/api_req_combined_battle/ld_airbattle")
				|| url.Contains("/kcsapi/api_req_sortie/airbattle")
				|| url.Contains("/kcsapi/api_req_combined_battle/airbattle");

			lock (Client.BattleStateLock)
			{
				if (url.Contains("/kcsapi/api_req_sortie/ld_airbattle")
					|| url.Contains("/kcsapi/api_req_combined_battle/ld_airbattle"))
				{
					Client.lastBattleApiType = "ld_airbattle";
				}
				else if (url.Contains("/kcsapi/api_req_sortie/battle")
					|| url.Contains("/kcsapi/api_req_combined_battle/battle"))
				{
					Client.lastBattleApiType = "battle";
				}
				else
				{
					Client.lastBattleApiType = null;
				}
			}

			var restrictToAir = Client.Settings?.ShowAirSuperiority ?? false;
			var showOnArrival = Client.Settings?.ShowCellOnArrival ?? false;

			bool shouldParseAir = showOnArrival
				? (!restrictToAir || isLdAirbattle)
				: false;

			AirSuperiority airResult = AirSuperiority.None;
			bool parsedBattleAir = false;
			if (!string.IsNullOrEmpty(normalized))
			{
				parsedBattleAir = TryParseAirSuperiority(normalized, out airResult, "BattleHandler.TryHandleBattle");
			}

			int cachedCellNo;
			lock (Client.BattleStateLock)
				cachedCellNo = Client.cachedCellNo;

			RunOnUi(() =>
			{
				try
				{
					if (cachedCellNo > 0)
					{
						var showOnArrivalLocal = Client.Settings?.ShowCellOnArrival ?? false;

						if (!showOnArrivalLocal)
						{
							Client.SortieInfo.EnterBattle(cachedCellNo);
						}
						else if (!Client.SortieInfo.CellNo.HasValue || Client.SortieInfo.CellNo.Value != cachedCellNo)
						{
							Client.SortieInfo.EnterBattle(cachedCellNo);
						}
					}

					Client.SortieInfo.IsLdAirbattle = isLdAirbattle;

					lock (Client.BattleStateLock)
					{
						if (shouldParseAir && parsedBattleAir)
						{
							Client.SortieInfo.SetAirResult(airResult);
							Client.hasPendingAirResult = false;
							Client.pendingAirResult = AirSuperiority.None;
						}
						else if (!shouldParseAir && parsedBattleAir)
						{
							Client.pendingAirResult = airResult;
							Client.hasPendingAirResult = true;
						}
					}
				}
				catch (Exception ex) { LogError("BattleHandler.TryHandleBattle", ex); }
			});

			return true;
		}

		// ── 戦闘結果 ──────────────────────────────────

		/// <summary>戦闘結果　BattleResult</summary>
		internal bool TryHandleBattleResult(string url, string normalized)
		{
			var isBattleResultApi =
				url.Contains("battleresult")
				&& (url.Contains("/kcsapi/api_req_sortie/")
					|| url.Contains("/kcsapi/api_req_combined_battle/")
					|| url.Contains("/kcsapi/api_req_battle_midnight/"));

			if (!isBattleResultApi)
				return false;

			Models.Raw.kcsapi_battleresult brLocal = null;
			Models.Raw.kcsapi_combined_battle_battleresult cbrLocal = null;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_battleresult>(normalized, out brLocal))
				{
				}
				else if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_combined_battle_battleresult>(normalized, out cbrLocal))
				{
				}
			}
			catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult", ex); }

			var showSortieInfo = Client.Settings?.ShowSortieInfo ?? false;
			var restrictToAir = Client.Settings?.ShowAirSuperiority ?? false;
			var showCellOnArrival = Client.Settings?.ShowCellOnArrival ?? false;

			string lastBattleApiType;
			lock (Client.BattleStateLock)
				lastBattleApiType = Client.lastBattleApiType;

			bool showAirResult = false;
			if (showCellOnArrival)
			{
				showAirResult = restrictToAir ? (lastBattleApiType == "ld_airbattle") : true;
			}
			else if (!showSortieInfo)
			{
				showAirResult = false;
			}
			else
			{
				showAirResult = restrictToAir ? (lastBattleApiType == "ld_airbattle") : true;
			}

			AirSuperiority airResult = AirSuperiority.None;
			bool parsedAir = false;
			if (showAirResult)
			{
				parsedAir = TryParseAirSuperiority(normalized, out airResult, "BattleHandler.TryHandleBattleResult");
			}

			string winRank = null;
			try
			{
				if (brLocal != null) winRank = brLocal.api_win_rank;
				else if (cbrLocal != null) winRank = cbrLocal.api_win_rank;
				else
				{
					try
					{
						var root = JToken.Parse(normalized);
						var data = root["api_data"] ?? root;
						winRank = data?["api_win_rank"]?.Value<string>();
					}
					catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult", ex); }
				}
			}
			catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult", ex); }

			// goback_port のために脱出情報を記録
			try
			{
				var jroot = JToken.Parse(normalized);
				var jdata = jroot["api_data"] ?? jroot;

				int escapeFlag = jdata?["api_escape_flag"]?.Value<int>() ?? 0;

				if (escapeFlag == 1)
				{
					int[] escapeIdxArr = null;
					int[] towIdxArr = null;

					try
					{
						var escapeIdxTok = jdata?["api_escape"]?["api_escape_idx"];
						if (escapeIdxTok != null && escapeIdxTok.Type == JTokenType.Array)
							escapeIdxArr = escapeIdxTok.ToObject<int[]>();
					}
					catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult/escape_idx", ex); }

					try
					{
						var towIdxTok = jdata?["api_escape"]?["api_tow_idx"];
						if (towIdxTok != null && towIdxTok.Type == JTokenType.Array)
							towIdxArr = towIdxTok.ToObject<int[]>();
					}
					catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult/tow_idx", ex); }

					if (escapeIdxArr != null && escapeIdxArr.Length > 0)
					{
						var org = Client.Homeport?.Organization;
						if (org != null)
						{
							Ship[] shipArray;
							int sortieDeckCount;
							lock (Client.BattleStateLock)
								sortieDeckCount = Client.sortieDeckIds.Count;

							if (sortieDeckCount >= 2)
							{
								shipArray = org.Fleets.OrderBy(f => f.Key)
									.Take(2)
									.SelectMany(f => f.Value.Ships)
									.ToArray();
							}
							else if (sortieDeckCount == 1)
							{
								int deckId;
								lock (Client.BattleStateLock)
									deckId = Client.sortieDeckIds.First();
								shipArray = org.Fleets.ContainsKey(deckId)
									? org.Fleets[deckId].Ships
									: new Ship[0];
							}
							else
							{
								shipArray = org.Fleets.ContainsKey(1) ? org.Fleets[1].Ships : new Ship[0];
							}

							lock (Client.BattleStateLock)
							{
								Client.pendingEscapeShipIds = escapeIdxArr
									.Where(idx => idx >= 1 && idx <= shipArray.Length)
									.Select(idx => shipArray[idx - 1].Id)
									.ToArray();

								Client.pendingTowShipIds = (towIdxArr != null && towIdxArr.Length > 0)
									? towIdxArr
										.Where(idx => idx >= 1 && idx <= shipArray.Length)
										.Select(idx => shipArray[idx - 1].Id)
										.ToArray()
									: new int[0];
							}

							System.Diagnostics.Debug.WriteLine(
								$"[BattleHandler.TryHandleBattleResult] escape: idx=[{string.Join(",", escapeIdxArr)}] " +
								$"escapeIds=[{string.Join(",", Client.pendingEscapeShipIds)}] " +
								$"towIds=[{string.Join(",", Client.pendingTowShipIds)}] " +
								$"shipArrayLen={shipArray.Length}");
						}
					}
					else
					{
						lock (Client.BattleStateLock)
						{
							Client.pendingEscapeShipIds = null;
							Client.pendingTowShipIds = null;
						}
					}
				}
				else
				{
					lock (Client.BattleStateLock)
					{
						Client.pendingEscapeShipIds = null;
						Client.pendingTowShipIds = null;
					}
				}
			}
			catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult/escape", ex); }

			RunOnUi(() =>
			{
				try
				{
					var org = Client.Homeport?.Organization;
					if (org == null) return;

					foreach (var fleet in org.Fleets.Values)
					{
						try { fleet.State.Update(); fleet.State.Calculate(); fleet.RaiseShipsUpdated(); } catch { }
					}

					try { Client.Homeport?.Organization?.NotifyUpdated(); } catch { }

					try
					{
						if (Application.Current != null && Application.Current.Dispatcher != null)
						{
							Application.Current.Dispatcher.InvokeAsync(() =>
							{
								try { Client.Homeport?.Organization?.NotifyUpdated(); } catch { }
							}, System.Windows.Threading.DispatcherPriority.Background);
						}
					}
					catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult", ex); }

					lock (Client.BattleStateLock)
					{
						if (parsedAir)
						{
							try { Client.SortieInfo.SetAirResult(airResult); } catch { }
							Client.hasPendingAirResult = false;
							Client.pendingAirResult = AirSuperiority.None;
						}
						else if (Client.hasPendingAirResult && showAirResult)
						{
							try { Client.SortieInfo.SetAirResult(Client.pendingAirResult); } catch { }
							Client.hasPendingAirResult = false;
							Client.pendingAirResult = AirSuperiority.None;
						}
					}

					if (!string.IsNullOrEmpty(winRank))
					{
						try { Client.SortieInfo.SetBattleResult(winRank); } catch { }
					}

					lock (Client.BattleStateLock)
						Client.lastBattleApiType = null;
				}
				catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult", ex); }
			});

			// EventMapHpViewer 等のプラグインへ battleresult を通知
			try
			{
				int cachedMapId;
				lock (Client.BattleStateLock)
					cachedMapId = Client.cachedMapId;
				if (cachedMapId > 0)
					Client.RaiseBattleResultReceived(cachedMapId, normalized);
			}
			catch (Exception ex) { LogError("BattleHandler.TryHandleBattleResult/BattleResultReceived", ex); }

			return true;
		}

		// ── 応急出港 ──────────────────────────────────

		/// <summary>応急出港 (goback_port)</summary>
		internal bool TryHandleGobackPort(string url)
		{
			if (!url.Contains("goback_port")) return false;

			try
			{
				int[] escapeIds;
				int[] towIds;
				lock (Client.BattleStateLock)
				{
					escapeIds = Client.pendingEscapeShipIds;
					towIds = Client.pendingTowShipIds;
				}

				if (escapeIds == null || escapeIds.Length == 0)
				{
					lock (Client.BattleStateLock)
					{
						Client.pendingEscapeShipIds = null;
						Client.pendingTowShipIds = null;
					}
					return true;
				}

				RunOnUi(() =>
				{
					try
					{
						var org = Client.Homeport?.Organization;
						if (org == null) return;

						int towId = (towIds != null && towIds.Length >= 1) ? towIds[0] : -1;
						org.AddEvacuatedShips(escapeIds[0], towId);
					}
					catch (Exception ex) { LogError("BattleHandler.TryHandleGobackPort", ex); }
					finally
					{
						lock (Client.BattleStateLock)
						{
							Client.pendingEscapeShipIds = null;
							Client.pendingTowShipIds = null;
						}
					}
				});
			}
			catch (Exception ex) { LogError("BattleHandler.TryHandleGobackPort", ex); }

			return true;
		}
	}
}
