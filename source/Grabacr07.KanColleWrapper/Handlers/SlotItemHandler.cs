using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 装備関連（純装備系）の API ハンドラーです。
	/// KanColleClient から呼び出され、Itemyard / Materials / Dockyard の状態更新を担当します。
	/// </summary>
	internal class SlotItemHandler
	{
		private readonly KanColleClient client;

		internal SlotItemHandler(KanColleClient client)
		{
			this.client = client;
		}

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		/// <summary>
		/// 装備廃棄
		/// </summary>
		internal bool TryHandleDestroyItem2(string url, string normalized, string requestBody)
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
							lock (this.client.SlotItemStateLock)
							{
								// api_get_material を増分として扱い、現在の Materials に加算して反映する
								var apiMat = di?.api_get_material;
								if (apiMat != null && apiMat.Length >= 4)
								{
									try
									{
										var materials = this.client.Homeport?.Materials;
										if (materials != null)
										{
											var abs = new int[4];
											abs[0] = materials.Fuel + (apiMat.Length > 0 ? apiMat[0] : 0);
											abs[1] = materials.Ammunition + (apiMat.Length > 1 ? apiMat[1] : 0);
											abs[2] = materials.Steel + (apiMat.Length > 2 ? apiMat[2] : 0);
											abs[3] = materials.Bauxite + (apiMat.Length > 3 ? apiMat[3] : 0);
											materials.Update(abs);
										}
									}
									catch (Exception)
									{
									}
								}

								// requestBody に api_slotitem_ids があれば装備を削除（CEF 経路であれば Itemyard の更新が届かないケースに対応）
								if (!string.IsNullOrEmpty(requestBody))
								{
									try
									{
										var dict = HandlerHelper.ParseRequestBody(requestBody);

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
														this.client.Homeport?.Itemyard?.SlotItems?.Remove(id);
													}
													catch (Exception)
													{
													}
												}
											}

											// Itemyard の内部通知を呼び出す（private メソッドをリフレクションで呼ぶ）
											try
											{
												try { this.client.Homeport?.Itemyard?.RaiseSlotItemsChanged(); }
												catch (Exception ex) { LogError("TryHandleDestroyItem2", ex); }
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

								// UI 再評価を促す
								try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }

								// カウンタープラグイン用イベント発火
								try { this.client.RaiseItemDestroyed(); } catch { }
							}
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
		/// 保有装備一覧の更新
		/// </summary>
		internal bool TryHandleSlotItems(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/slot_item")) return false;
			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<Models.Raw.kcsapi_slotitem[]>(normalized, out var slotItems))
				{
					RunOnUi(() =>
					{
						try
						{
							lock (this.client.SlotItemStateLock)
							{
								this.client.Homeport.Itemyard.Update(slotItems);
							}
						}
						catch (Exception ex) { LogError("TryHandleSlotItems", ex); }
					});
				}
				else
				{
				}
			}
			catch (Exception ex) { LogError("TryHandleSlotItems", ex); }
			return true;
		}

		/// <summary>
		/// 開発
		/// </summary>
		internal bool TryHandleCreateItem(string url, string normalized, string requestBody)
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
					lock (this.client.SlotItemStateLock)
					{
						// 1) 資源・資材更新: api_material が 4 か 8 長配列で来る場合の柔軟対応
						try
						{
							var matTok = data["api_material"] ?? data["api_get_material"] ?? data["api_materials"];
							if (matTok != null && matTok.Type == JTokenType.Array)
							{
								var arr = matTok.Select(t => (int?)t ?? 0).ToArray();
								var materials = this.client.Homeport?.Materials;
								if (materials != null && arr != null)
									materials.UpdateFull(arr);
							}
						}
						catch (Exception ex) { LogError("TryHandleCreateItem", ex); }

						// 2) 生成された装備を反映: api_get_items / api_slot_item / api_slotitem などの複合対応
						try
						{
							var iy = this.client.Homeport?.Itemyard;
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
											catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
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
												catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
											}

											try { iy.RaiseSlotItemsChanged(); }
											catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
										}
									}
									catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
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
													HandlerHelper.UpsertSlotItem(iy, r, "TryHandleCreateItem");
												}
												catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
											}

											try
											{
												iy?.RaiseSlotItemsChanged();
											}
											catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
										}
									}
									catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
								}

								// 3) api_unset_items / api_unset_list があれば UI 再描画通知
								try
								{
									var unsetTok = data["api_unset_items"] ?? data["api_unset_list"] ?? data["api_unset_slot"];
									if (unsetTok != null)
									{
										try { iy.RaiseSlotItemsChanged(); } catch { }
									}
								}
								catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
							}
						}
						catch (Exception ex) { LogError("TryHandleCreateItem", ex); }

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
									var dockyard = this.client.Homeport?.Dockyard;
									if (dockyard != null)
									{
										try
										{
											// Dockyard.CreateSlotItem に相当する処理は内部 private のため簡易に CreatedSlotItem を作る
											dockyard.CreatedSlotItem = new CreatedSlotItem(createTok);
										}
										catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
									}
								}
							}
							catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
						}
						catch (Exception ex) { LogError("TryHandleCreateItem", ex); }

						// 最後に UI 全体更新を促す
						try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
					}
				}
				catch (Exception ex) { LogError("TryHandleCreateItem", ex); }
			});

			return true;
		}

		/// <summary>
		/// 装備改修
		/// </summary>
		internal bool TryHandleRemodelSlot(string url, string normalized, string requestBody)
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
							lock (this.client.SlotItemStateLock)
							{
								var iy = this.client.Homeport?.Itemyard;
								var materials = this.client.Homeport?.Materials;

								// 1) 資源反映 (api_after_material)
								try
								{
									if (rem.api_after_material != null && materials != null)
									{
										materials.UpdateFull(rem.api_after_material);
									}
								}
								catch (Exception ex) { LogError("TryHandleRemodelSlot", ex); }

								// 2) api_after_slot の反映（生成・改修された装備）
								try
								{
									if (rem.api_after_slot != null && iy != null)
									{
										var a = rem.api_after_slot;
										var raw = new kcsapi_slotitem
										{
											api_id = a.api_id,
											api_slotitem_id = a.api_slotitem_id,
											api_level = a.api_level,
											api_locked = a.api_locked,
											api_alv = 0
										};

										HandlerHelper.UpsertSlotItem(iy, raw, "TryHandleRemodelSlot");

										try { iy.RaiseSlotItemsChanged(); } catch { }
									}
								}
								catch (Exception ex) { LogError("TryHandleRemodelSlot", ex); }

								// 3) api_use_slot_id: 使用（消費）された装備 ID の削除
								try
								{
									if (rem.api_use_slot_id != null && rem.api_use_slot_id.Length > 0 && iy != null)
									{
										foreach (var id in rem.api_use_slot_id)
										{
											try { iy.SlotItems.Remove(id); } catch { }
										}
										try { iy.RaiseSlotItemsChanged(); } catch { }
									}
								}
								catch (Exception ex) { LogError("TryHandleRemodelSlot", ex); }

								// 最後に組織レベルの更新通知で UI を確実に再描画
								try { this.client.Homeport?.Organization?.NotifyUpdated(); } catch { }
							}
						}
						catch (Exception ex) { LogError("TryHandleRemodelSlot", ex); }
					});

					return true;
				}
			}
			catch (Exception ex) { LogError("TryHandleRemodelSlot", ex); }

			return true;
		}
	}
}
