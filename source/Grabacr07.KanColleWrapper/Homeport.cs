using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // 追加
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Windows; // 追加
using System.Web;

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// 母港を表します。
	/// </summary>
	public class Homeport : Notifier
	{
		/// <summary>
		/// 艦隊の編成状況にアクセスできるようにします。
		/// </summary>
		public Organization Organization { get; }

		/// <summary>
		/// 資源および資材の保有状況にアクセスできるようにします。
		/// </summary>
		public Materials Materials { get; }

		/// <summary>
		/// 装備や消費アイテムの保有状況にアクセスできるようにします。
		/// </summary>
		public Itemyard Itemyard { get; }

		/// <summary>
		/// 複数の建造ドックを持つ工廠を取得します。
		/// </summary>
		public Dockyard Dockyard { get; }

		/// <summary>
		/// 複数の入渠ドックを持つ工廠を取得します。
		/// </summary>
		public Repairyard Repairyard { get; }

		/// <summary>
		/// 任務情報を取得します。
		/// </summary>
		public Quests Quests { get; }

		/// <summary>
		/// 基地航空隊（航空隊）の情報を取得します。
		/// </summary>
		public AirBases AirBases { get; }

		// UI スレッドへ安全に実行するヘルパー
		private static void RunOnUi(Action action)
		{
			try
			{
				if (Application.Current != null && Application.Current.Dispatcher != null)
				{
					Application.Current.Dispatcher.BeginInvoke(action);
				}
				else
				{
					action();
				}
			}
			catch
			{
				try { action(); } catch { }
			}
		}

		#region Admiral 変更通知プロパティ

		private Admiral _Admiral;

		/// <summary>
		/// 現在ログインしている提督を取得します。
		/// <see cref="INotifyPropertyChanged.PropertyChanged"/> イベントによる変更通知をサポートします。
		/// </summary>
		public Admiral Admiral
		{
			get { return this._Admiral; }
			private set
			{
				if (this._Admiral != value)
				{
					this._Admiral = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		internal Homeport(KanColleProxy proxy)
		{
			this.Materials = new Materials(proxy);
			this.Itemyard = new Itemyard(this, proxy);
			this.Organization = new Organization(this, proxy);
			this.Repairyard = new Repairyard(this, proxy);
			this.Dockyard = new Dockyard(proxy);
			this.Quests = new Quests(proxy);
			this.AirBases = new AirBases();

			// port は UI スレッドで反映する
			proxy.api_port.TryParse<kcsapi_port>().Subscribe(x =>
			{
				RunOnUi(() =>
				{
					this.UpdateAdmiral(x.Data.api_basic);
					this.Organization.Update(x.Data.api_ship);
					this.Repairyard.Update(x.Data.api_ndock);
					this.Organization.Update(x.Data.api_deck_port);
					this.Organization.Combined = x.Data.api_combined_flag != 0;
					this.Materials.Update(x.Data.api_material);
				});
			});

			proxy.ApiSessionSource.Subscribe(session =>
			{
				try
				{
					var path = session.Request?.PathAndQuery ?? "<null>";
					var len = session.Response?.Body?.Length ?? 0;

					if (len <= 0) return;

					// バイト列 → 文字列
					var raw = System.Text.Encoding.UTF8.GetString(session.Response.Body);
					raw = Internal.Extensions.NormalizeSvDataString(raw) ?? raw;

					// 航空隊 / mapinfo を含むレスポンスを検出してパース
					if (raw.Contains("\"api_air_base\"") || raw.Contains("\"api_map_info\""))
					{
						JToken root = null;
						try { root = JToken.Parse(raw); } catch { root = null; }
						if (root == null) return;

						var data = root["api_data"] ?? root;
						if (data == null) return;

						var airBaseTok = data["api_air_base"];
						var expandedTok = data["api_air_base_expanded_info"];

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
									try
									{

										this.AirBases?.Update(ab, abi);
									}
									catch
									{
									}
								});
							}
						}
					}
				}
				catch
				{
				}
			});

			// 生セッションから api_air_base 系を取り出して AirBases を更新する
			proxy.api_port.Subscribe(session =>
			{
				try
				{
					// Session の Response.Body を参照してレスポンスボディを取得
					var responseBody = session.Response.Body;
					if (responseBody == null || responseBody.Length == 0) return;

					// レスポンスボディを文字列に変換
					var raw = System.Text.Encoding.UTF8.GetString(responseBody);
					if (string.IsNullOrEmpty(raw)) return;

					// "svdata=" プレフィックスを削除（ゲーム API の標準フォーマット）
					raw = Internal.Extensions.NormalizeSvDataString(raw) ?? raw;

					JToken root = null;
					try { root = JToken.Parse(raw); } catch { root = null; }
					if (root == null) return;

					var data = root["api_data"] ?? root;
					if (data == null) return;

					var airBaseTok = data["api_air_base"] ?? data.SelectToken("api_air_base");
					if (airBaseTok == null) return;

					var airBaseExpandedTok = data["api_air_base_expanded_info"] ?? data.SelectToken("api_air_base_expanded_info");

					kcsapi_air_base[] ab = null;
					kcsapi_air_base_expanded_info[] abi = null;

					try { ab = airBaseTok.ToObject<kcsapi_air_base[]>(); } catch { ab = null; }
					try { abi = airBaseExpandedTok?.ToObject<kcsapi_air_base_expanded_info[]>(); } catch { abi = null; }

					if (ab != null)
					{
						RunOnUi(() =>
						{
							try
							{
								this.AirBases?.Update(ab, abi);
							}
							catch
							{
							}
						});
					}
				}
				catch
				{
				}
			});

			// --- 追加: change_name / set_action の成功レスポンスを検知して即時反映 ---
			proxy.ApiSessionSource
				.Where(s => (s.Request?.PathAndQuery ?? "").StartsWith("/kcsapi/api_req_air_corps/", StringComparison.OrdinalIgnoreCase))
				.Subscribe(session =>
				{
					try
					{
						// レスポンスを svdata として解析し、成功フラグを確認する
						SvData sv;
						if (!SvData.TryParse(session, out sv)) return;
						if (!sv.IsSuccess) return;

						// リクエストボディからパラメータを取得
						var body = session.Request?.BodyAsString ?? session.Request?.BodyAsString ?? "";
						var q = HttpUtility.ParseQueryString(body);

						// path で振り分け
						var path = session.Request?.PathAndQuery ?? "";

						if (path.IndexOf("/change_name", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							// 想定パラメータ: api_area_id, api_base_id, api_name
							int areaId = ParseIntFromQuery(q, "api_area_id", "api_area");
							int baseId = ParseIntFromQuery(q, "api_base_id", "api_baseid", "api_rid");
							var name = q["api_name"] ?? q["name"] ?? "";

							if (areaId > 0 && baseId > 0)
							{
								RunOnUi(() =>
								{
									try
									{
										this.AirBases?.ApplyChangeName(areaId, baseId, name);
									}
									catch
									{
									}
								});
							}
						}
						else if (path.IndexOf("/set_action", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							// 想定パラメータ: api_area_id, api_base_id, api_action_kind
							int areaId = ParseIntFromQuery(q, "api_area_id", "api_area");
							int baseId = ParseIntFromQuery(q, "api_base_id", "api_baseid", "api_rid");
							int actionKind = ParseIntFromQuery(q, "api_action_kind", "api_action", "action_kind");

							if (areaId > 0 && baseId > 0)
							{
								RunOnUi(() =>
								{
									try
									{
										this.AirBases?.ApplySetAction(areaId, baseId, actionKind);
									}
									catch
									{
									}
								});
							}
						}
					}
					catch
					{
					}
				});

			// 個別 basic も UI スレッドで反映
			proxy.api_get_member_basic.TryParse<kcsapi_basic>().Subscribe(x =>
			{
				RunOnUi(() => this.UpdateAdmiral(x.Data));
			});

			proxy.api_req_member_updatecomment.TryParse().Subscribe(this.UpdateComment);
		}

		private static int ParseIntFromQuery(System.Collections.Specialized.NameValueCollection q, params string[] keys)
		{
			foreach (var k in keys)
			{
				var v = q[k];
				int n;
				if (!string.IsNullOrEmpty(v) && int.TryParse(v, out n)) return n;
			}
			return 0;
		}

		internal void UpdateAdmiral(kcsapi_basic data)
		{
			this.Admiral = new Admiral(data);
		}

		private void UpdateComment(SvData data)
		{
			if (data == null || !data.IsSuccess) return;

			try
			{
				this.Admiral.Comment = data.Request["api_cmt"];
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("艦隊名の変更に失敗しました: {0}", ex);
			}
		}

		internal void StartConditionCount()
		{
			//Observable.Timer(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3))
		}

	}
}
