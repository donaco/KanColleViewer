using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 提督・任務・アイテム系の API ハンドラーです。
	/// KanColleClient から呼び出され、Homeport 配下の各情報更新を担当します。
	/// </summary>
	internal class BasicHandler
	{
		private readonly KanColleClient client;

		internal BasicHandler(KanColleClient client)
		{
			this.client = client;
		}

		private void RunOnUi(Action action)
			=> HandlerHelper.RunOnUi(action);

		private static void LogError(string context, Exception ex)
			=> HandlerHelper.LogError(context, ex);

		/// <summary>
		/// 提督情報 (api_get_member/basic)
		/// </summary>
		internal bool TryHandleBasic(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/basic")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_basic>(normalized, out var basic))
				{
					RunOnUi(() =>
					{
						try
						{
							this.client.Homeport?.UpdateAdmiral(basic);
						}
						catch (Exception ex) { LogError("TryHandleBasic", ex); }
					});
				}
			}
			catch (Exception ex) { LogError("TryHandleBasic", ex); }

			return true;
		}

		/// <summary>
		/// 任務完了 + 資源・アイテム更新
		/// </summary>
		internal bool TryHandleClearItemGet(string url, string normalized)
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
					catch (Exception ex) { apiMaterialArray = null; LogError("TryHandleClearItemGet", ex); }
				}

				// 装備枠の増加を推測して即時反映
				try
				{
					var bonusTok = data["api_bounus"] ?? data["api_bonus"];
					if (bonusTok != null && bonusTok.Type == JTokenType.Array)
					{
						int deltaCapacity = 0;
						foreach (var b in bonusTok.Children())
						{
							try
							{
								// 安全にフィールドを抽出（api_count / api_type / api_item.api_id 等）
								var typeTok = b["api_type"];
								var countTok = b["api_count"];
								var itemTok = b["api_item"] ?? b["api_item_id"];

								int type = typeTok?.Value<int>() ?? -1;
								int count = countTok?.Value<int>() ?? 0;
								int itemId = 0;
								if (itemTok != null)
								{
									// api_item がオブジェクトの場合と単純値の場合の両対応
									if (itemTok.Type == JTokenType.Object)
										itemId = itemTok["api_id"]?.Value<int>() ?? 0;
									else if (itemTok.Type == JTokenType.Integer)
										itemId = itemTok.Value<int>();
								}

								// api_id: 901～940 の場合、下2桁が装備枠増加数を表すと推測
								// 例: 901 → +1, 902 → +2, 912 → +12, 920 → +20 など
								if (itemId >= 901 && itemId <= 940)
								{
									int slotIncrease = itemId - 900;
									deltaCapacity += slotIncrease;
								}
							}
							catch (Exception ex) { LogError("TryHandleClearItemGet", ex); }
						}

						if (deltaCapacity > 0)
						{
							// Admiral.api_max_slotitem を安全に増加させて UI に即時反映する
							RunOnUi(() =>
							{
								try
								{
									var adm = this.client.Homeport?.Admiral;
									if (adm == null) return;

									// kcsapi_basic をクローンして api_max_slotitem を増加させ、Homeport.UpdateAdmiral で置換する。
									// 直接 RawData を書き換えるより安定して通知が飛ぶ。
									try
									{
										var json = JsonConvert.SerializeObject(adm.RawData);
										var cloned = JsonConvert.DeserializeObject<kcsapi_basic>(json);
										if (cloned != null)
										{
											cloned.api_max_slotitem = (cloned.api_max_slotitem) + deltaCapacity;
											this.client.Homeport.UpdateAdmiral(cloned);
										}
									}
									catch (Exception ex) { LogError("TryHandleClearItemGet", ex); }
								}
								catch (Exception ex) { LogError("TryHandleClearItemGet", ex); }
							});
						}
					}
				}
				catch (Exception ex) { LogError("TryHandleClearItemGet", ex); }

				// UI スレッドで安全に反映
				if (apiMaterialArray != null)
				{
					RunOnUi(() =>
					{
						try
						{
							var materials = this.client.Homeport?.Materials;
							HandlerHelper.ApplyMaterialDelta(materials, apiMaterialArray, "TryHandleClearItemGet");
						}
						catch (Exception ex) { LogError("TryHandleClearItemGet", ex); }
					});
				}

				// ボーナスアイテム(api_bounus) は後続の /api_get_member/slot_item 等で反映されることが多い。
				// 複雑なパターンは別ハンドラに任せるためここでは UI 更新を促すだけにとどめる。
				RunOnUi(() =>
				{
					try
					{
						this.client.Homeport?.Organization?.NotifyUpdated();
					}
					catch (Exception ex) { LogError("TryHandleClearItemGet", ex); }
				});
			}
			catch (Exception ex) { LogError("TryHandleClearItemGet", ex); }

			return true;
		}

		/// <summary>
		/// 資源
		/// </summary>
		internal bool TryHandleMaterial(string url, string normalized)
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
							this.client.Homeport?.Materials.Update(mats);
						}
						catch (Exception ex) { LogError("TryHandleMaterial", ex); }
					});
				}
			}
			catch (Exception ex) { LogError("TryHandleMaterial", ex); }

			return true;
		}

		/// <summary>
		/// アイテム使用
		/// </summary>
		internal bool TryHandleUseItem(string url, string normalized)
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
							this.client.Homeport?.Itemyard.Update(useitems);
						}
						catch (Exception ex) { LogError("TryHandleUseItem", ex); }
					});
				}
			}
			catch (Exception ex) { LogError("TryHandleUseItem", ex); }

			return true;
		}

		/// <summary>
		/// 任務一覧
		/// </summary>
		internal bool TryHandleQuestList(string url, string normalized)
		{
			if (!url.Contains("/kcsapi/api_get_member/questlist")) return false;

			try
			{
				if (ApiDataDeserializer.TryDeserializeApiData<kcsapi_questlist>(normalized, out var questlist))
				{
					RunOnUi(() =>
					{
						try
						{
							this.client.Homeport.Quests.Update(questlist);
						}
						catch (Exception ex) { LogError("TryHandleQuestList", ex); }
					});
				}
			}
			catch (Exception ex) { LogError("TryHandleQuestList", ex); }

			return true;
		}

		/// <summary>
		/// コメント更新 (api_req_member/updatecomment)
		/// </summary>
		internal bool TryHandleUpdateComment(string url, string normalized, string requestBody)
		{
			if (!url.Contains("/kcsapi/api_req_member/updatecomment")) return false;
			try
			{
				if (string.IsNullOrEmpty(requestBody)) return true;

				var dict = HandlerHelper.ParseRequestBody(requestBody, StringComparer.OrdinalIgnoreCase);
				var comment = dict.TryGetValue("api_cmt", out var cmt) ? cmt : "";

				RunOnUi(() =>
				{
					try { this.client.Homeport?.UpdateComment(comment); }
					catch (Exception ex) { LogError("TryHandleUpdateComment", ex); }
				});
			}
			catch (Exception ex) { LogError("TryHandleUpdateComment", ex); }
			return true;
		}
	}
}
