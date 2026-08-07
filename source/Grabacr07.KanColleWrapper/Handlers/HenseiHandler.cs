using Grabacr07.KanColleWrapper.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 編成関連の API ハンドラーです。
	/// KanColleClient から呼び出され、Organization（艦隊編成）の状態更新を担当します。
	/// </summary>
	internal class HenseiHandler
	{
		private readonly KanColleClient client;

		internal HenseiHandler(KanColleClient client)
		{
			this.client = client;
		}

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		/// <summary>
		/// 艦隊の解除処理（respChangeCount 分の Unset）を適用します。
		/// 全解除に相当する場合は UnsetAll、部分解除の場合は末尾から respChangeCount 個を Unset します。
		/// 該当条件に一致し処理した場合は true を返します。
		/// </summary>
		private static bool ApplyDeckChangeCount(Fleet fleet, Organization org, int respChangeCount)
		{
			int nonEmpty = fleet.Ships.Count(s => s != null && s.Id > 0);

			// ① 全解除に相当
			if (respChangeCount >= nonEmpty && nonEmpty > 0)
			{
				try { fleet.UnsetAll(); } catch { }
				try { fleet.RaiseShipsUpdated(); } catch { }
				try { org.NotifyUpdated(); } catch { }
				return true;
			}

			// ② 部分解除：レスポンスだけの場合は末尾から消えることが多いのでヒューリスティックで解除
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
				return true;
			}

			return false;
		}

		/// <summary>
		/// 編成系1
		/// </summary>
		internal bool TryHandleDecks(string url, string normalized, string requestBody)
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
						catch (Exception ex) { LogError("TryHandleDecks", ex); }

						// requestBody があれば api_id を取り、なければ lastChangeDeckId をフォールバックで使う
						int deckId = -1;
						if (!string.IsNullOrEmpty(requestBody))
						{
							var dict = HandlerHelper.ParseRequestBody(requestBody);
							if (dict.TryGetValue("api_id", out var idStr) && int.TryParse(idStr, out var id))
							{
								deckId = id;
							}
						}

						lock (this.client.HenseiStateLock)
						{
							if (deckId == -1 && this.client.lastChangeDeckId != -1) deckId = this.client.lastChangeDeckId;
						}

						// deckId が確定していなければここでは処理しない（その他のハンドラにフォールバック）
						if (deckId == -1) { /* fallthrough to other handling below */ }
						else if (respChangeCount > 0)
						{
							// UI 更新は UI スレッドで行う
							RunOnUi(() =>
							{
								try
								{
									var org = this.client.Homeport?.Organization;
									if (org == null || !org.Fleets.ContainsKey(deckId)) return;
									var fleet = org.Fleets[deckId];

									ApplyDeckChangeCount(fleet, org, respChangeCount);
								}
								catch (Exception)
								{
								}
							});

							// 既に change レスポンスを処理したので TryHandleDecks 全体として true を返す
							return true;
						}
					}
					catch (Exception)
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
									try { this.client.Homeport.Organization.Update(deck); } catch { }
								}
							}

							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandleDecks", ex); }
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
							this.client.Homeport.Organization.Update(singleDeck);
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandleDecks", ex); }
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
							lock (this.client.HenseiStateLock)
							{
								this.client.lastChangeDeckId = deckId;
							}
						}
						else
						{
							lock (this.client.HenseiStateLock)
							{
								if (this.client.lastChangeDeckId != -1)
								{
									deckId = this.client.lastChangeDeckId;
								}
								else
								{
									return true;
								}
							}
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
						catch (Exception ex) { LogError("TryHandleDecks", ex); }

						RunOnUi(() =>
						{
							try
							{
								var org = this.client.Homeport?.Organization;
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
									if (ApplyDeckChangeCount(fleet, org, respChangeCount))
									{
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
										catch (Exception ex) { LogError("TryHandleDecks", ex); }
									}

									try { fleet.RaiseShipsUpdated(); } catch { }
									try
									{
										HandlerHelper.RefreshAllFleets(this.client.Homeport);
									}
									catch (Exception ex) { LogError("TryHandleDecks", ex); }

									try { org.NotifyUpdated(); } catch { }
									return;
								}
							}
							catch (Exception ex) { LogError("TryHandleDecks", ex); }
						});
					}
					catch (Exception)
					{
						// swallow
					}
				}
			}
			catch (Exception)
			{
			}

			return true;
		}

		/// <summary>
		/// 編成系2　プリセット編成取得
		/// </summary>
		internal bool TryHandlePresetDeck(string url, string normalized)
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
									try { this.client.Homeport.Organization.Update(deck); } catch { }
								}
							}
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandlePresetDeck", ex); }
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
							this.client.Homeport.Organization.Update(single);
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandlePresetDeck", ex); }
					});
					return true;
				}
			}
			catch (Exception)
			{
			}
			return true; // マッチしたが解析失敗でも早期 return（既存ハンドラと同挙動）
		}

		/// <summary>
		/// 編成系3　プリセット編成実行
		/// </summary>
		internal bool TryHandlePresetSelect(string url, string normalized)
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
							this.client.Homeport.Organization.Update(deck);
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandlePresetSelect", ex); }
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
							this.client.Homeport.Organization.Update(built);
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandlePresetSelect", ex); }
					});
				}
			}
			catch (Exception)
			{
				// swallow
			}

			return true;
		}

		/// <summary>
		/// 編成系4　連合艦隊
		/// </summary>
		internal bool TryHandleHenseiCombined(string url, string normalized)
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
							this.client.Homeport.Organization.Combined = (data?.api_combined ?? 0) != 0;
							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception ex) { LogError("TryHandleHenseiCombined", ex); }
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
						this.client.Homeport.Organization.Combined = combined != 0;
						HandlerHelper.RefreshAllFleets(this.client.Homeport);
					}
					catch (Exception ex) { LogError("TryHandleHenseiCombined", ex); }
				});

				return true;
			}
			catch (Exception)
			{
				// swallow
			}
			return true;
		}

		/// <summary>
		/// 編成系5　艦隊名の編集
		/// </summary>
		internal bool TryHandleUpdatedeckname(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_member/updatedeckname")) return false;

			try
			{
				// レスポンスは成功 JSON のみでデータを返さないことが多いので requestBody を参照して即時反映する
				if (string.IsNullOrEmpty(requestBody)) return true;

				var dict = HandlerHelper.ParseRequestBody(requestBody, StringComparer.OrdinalIgnoreCase);

				if (!dict.ContainsKey("api_deck_id")) return true;
				if (!int.TryParse(dict["api_deck_id"], out var deckId)) return true;
				var name = dict.ContainsKey("api_name") ? dict["api_name"] : string.Empty;

				RunOnUi(() =>
				{
					try
					{
						var org = this.client.Homeport?.Organization;
						if (org == null) return;
						if (!org.Fleets.ContainsKey(deckId)) return;
						var fleet = org.Fleets[deckId];

						// 直接艦隊名を更新して通知
						try { fleet.Name = name; } catch { }
						try { fleet.RaiseShipsUpdated(); } catch { }
						try { org.NotifyUpdated(); } catch { }
					}
					catch (Exception)
					{
					}
				});
			}
			catch (Exception)
			{
				// swallow
			}

			return true;
		}

		/// <summary>
		/// ship_deck（編成系の一部・所属艦隊情報の取得）
		/// </summary>
		internal bool TryHandleShipDeck(string url, string normalized)
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
							this.client.Homeport.Organization.Update(shipDeck);

							HandlerHelper.RefreshAllFleets(this.client.Homeport);
						}
						catch (Exception)
						{
						}
					});
				}
				else
				{
				}
			}
			catch (Exception)
			{
			}
			return true;
		}
	}
}
