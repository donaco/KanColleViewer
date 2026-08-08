using Grabacr07.KanColleWrapper.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Grabacr07.KanColleWrapper.Handlers
{
	/// <summary>
	/// 各種ハンドラーから共通で利用するユーティリティをまとめた静的クラスです。
	/// UI スレッドへの入口 (<see cref="RunOnUi(Action)"/>) はここに統一します。
	/// </summary>
	internal static class HandlerHelper
	{
		/// <summary>
		/// 例外をログに記録します。アプリを落とさないよう、再スローはしません。
		/// </summary>
		/// <param name="context">どの処理で発生したかを示す文字列（例: メソッド名や API パス）</param>
		/// <param name="ex">発生した例外</param>
		internal static void LogError(string context, Exception ex)
		{
			try
			{
				System.Diagnostics.Debug.WriteLine($"[KanColleClient] Error in {context}: {ex}");
			}
			catch
			{
				// ログ出力自体が失敗しても何もしない
			}
		}

		/// <summary>
		/// UI スレッドへ安全にアクションを実行するヘルパーです。
		/// UI 更新の入口はここに統一します。
		/// </summary>
		internal static void RunOnUi(Action action)
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
			catch (Exception ex) { LogError("RunOnUi", ex); }
		}

		/// <summary>
		/// URL エンコードされたリクエスト Body を辞書にパースします。
		/// </summary>
		internal static IReadOnlyDictionary<string, string> ParseRequestBody(string requestBody)
		{
			return ParseRequestBody(requestBody, null);
		}

		/// <summary>
		/// URL エンコードされたリクエスト Body を辞書にパースします。
		/// キーの比較方法を指定できます（例: <see cref="StringComparer.OrdinalIgnoreCase"/>）。
		/// </summary>
		/// <param name="requestBody">パース対象のリクエスト Body</param>
		/// <param name="keyComparer">キーの比較子。null の場合は既定の比較（大文字小文字を区別）</param>
		internal static IReadOnlyDictionary<string, string> ParseRequestBody(string requestBody, IEqualityComparer<string> keyComparer)
		{
			var dict = keyComparer == null
				? new Dictionary<string, string>()
				: new Dictionary<string, string>(keyComparer);
			if (string.IsNullOrEmpty(requestBody)) return dict;
			foreach (var p in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var kv = p.Split(new[] { '=' }, 2);
				if (kv.Length == 2)
					dict[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
			}
			return dict;
		}

		/// <summary>
		/// 資源の増分（燃料・弾薬・鋼材・ボーキサイト）を現在値に加算して反映します。
		/// 増分がすべて 0 の場合や資材が未取得の場合は何もしません。
		/// </summary>
		/// <param name="materials">更新対象の資材</param>
		/// <param name="addMaterials">4 要素の増分配列（負値を指定すれば減算になります）</param>
		/// <param name="contextForLog">失敗時にログへ記録する処理名</param>
		internal static void ApplyMaterialDelta(Materials materials, int[] addMaterials, string contextForLog)
		{
			try
			{
				if (materials == null || addMaterials == null || addMaterials.Length < 4) return;
				if (addMaterials[0] == 0 && addMaterials[1] == 0 && addMaterials[2] == 0 && addMaterials[3] == 0) return;

				var abs = new int[4];
				abs[0] = materials.Fuel + addMaterials[0];
				abs[1] = materials.Ammunition + addMaterials[1];
				abs[2] = materials.Steel + addMaterials[2];
				abs[3] = materials.Bauxite + addMaterials[3];
				materials.Update(abs);
			}
			catch (Exception ex) { LogError(contextForLog, ex); }
		}

		/// <summary>
		/// 資源の消費分（燃料・弾薬・鋼材・ボーキサイト）を現在値から差し引いて反映します。
		/// 負の値にならないよう 0 でガードします。
		/// 消費がすべて 0 以下の場合や資材が未取得の場合は何もしません。
		/// </summary>
		/// <param name="materials">更新対象の資材</param>
		/// <param name="consume">消費分の配列（4 要素未満でも不足分は 0 として扱います）</param>
		/// <param name="contextForLog">失敗時にログへ記録する処理名</param>
		internal static void ApplyMaterialConsumption(Materials materials, int[] consume, string contextForLog)
		{
			try
			{
				if (materials == null || consume == null) return;

				var c0 = consume.Length > 0 ? consume[0] : 0;
				var c1 = consume.Length > 1 ? consume[1] : 0;
				var c2 = consume.Length > 2 ? consume[2] : 0;
				var c3 = consume.Length > 3 ? consume[3] : 0;
				if (c0 <= 0 && c1 <= 0 && c2 <= 0 && c3 <= 0) return;

				var newMat = new int[4];
				newMat[0] = Math.Max(0, materials.Fuel - c0);
				newMat[1] = Math.Max(0, materials.Ammunition - c1);
				newMat[2] = Math.Max(0, materials.Steel - c2);
				newMat[3] = Math.Max(0, materials.Bauxite - c3);
				materials.Update(newMat);
			}
			catch (Exception ex) { LogError(contextForLog, ex); }
		}

		/// <summary>
		/// 全艦隊の状態を再計算し、UI へ更新を通知します。
		/// 個々の艦隊で例外が発生しても後続の艦隊の処理は継続します。
		/// </summary>
		/// <param name="homeport">対象の母港</param>
		internal static void RefreshAllFleets(Homeport homeport)
		{
			try { homeport?.Organization?.NotifyUpdated(); } catch { }

			var org = homeport?.Organization;
			if (org == null) return;

			foreach (var f in org.Fleets.Values)
			{
				try { f.State.Calculate(); f.State.Update(); f.RaiseShipsUpdated(); } catch { }
			}
		}

		/// <summary>
		/// 指定した艦娘が所属する艦隊を取得し、状態の再計算と再通知を行います。
		/// 呼び出し順序は既存実装の挙動を維持するため <paramref name="calculateFirst"/> で指定します。
		/// </summary>
		/// <param name="org">対象の艦隊組織</param>
		/// <param name="shipId">対象艦娘の ID</param>
		/// <param name="calculateFirst">true の場合 Calculate → Update、false の場合 Update → Calculate の順で呼び出す</param>
		/// <param name="caller">失敗時にログへ記録する処理名</param>
		internal static void RefreshFleetByShipId(Organization org, int shipId, bool calculateFirst, string caller)
		{
			if (org == null) return;

			Fleet fleet;
			try { fleet = org.GetFleet(shipId); }
			catch (Exception ex) { LogError(caller, ex); return; }

			if (fleet == null) return;

			if (calculateFirst)
			{
				try { fleet.State.Calculate(); } catch (Exception ex) { LogError(caller, ex); }
				try { fleet.State.Update(); } catch (Exception ex) { LogError(caller, ex); }
			}
			else
			{
				try { fleet.State.Update(); } catch (Exception ex) { LogError(caller, ex); }
				try { fleet.State.Calculate(); } catch (Exception ex) { LogError(caller, ex); }
			}

			try { fleet.RaiseShipsUpdated(); } catch (Exception ex) { LogError(caller, ex); }
		}

		#region 制空共通処理

		/// <summary>
		/// レスポンス JSON から制空状態を解析します。
		/// 制空情報が取得できた場合のみ true を返します。
		/// </summary>
		internal static bool TryParseAirSuperiority(string normalized, out AirSuperiority airResult, string contextForLog)
		{
			airResult = AirSuperiority.None;
			if (string.IsNullOrEmpty(normalized)) return false;

			try
			{
				var root = JToken.Parse(normalized);
				var data = root["api_data"] ?? root;
				return TryParseAirSuperiorityFromApiData(data, out airResult);
			}
			catch (Exception ex)
			{
				LogError(contextForLog, ex);
				return false;
			}
		}

		/// <summary>
		/// api_data トークンから制空状態を解析します。
		/// </summary>
		internal static bool TryParseAirSuperiorityFromApiData(JToken data, out AirSuperiority airResult)
		{
			airResult = AirSuperiority.None;
			if (data == null) return false;

			var kouku = data.SelectToken("api_kouku");
			var planeFrom = kouku?["api_plane_from"];

			// 制空戦が発生していない場合
			if (planeFrom == null || !planeFrom.Any(t => t.HasValues))
			{
				return false;
			}

			var stage1 = data.SelectToken("api_kouku.api_stage1");
			var dispSeiku = stage1?["api_disp_seiku"];
			if (dispSeiku == null) return false;

			int val;
			if (int.TryParse(dispSeiku.ToString(), out val) && val >= 0 && val <= 4)
			{
				airResult = (AirSuperiority)val;
				return true;
			}

			return false;
		}

		#endregion

		/// <summary>
		/// 装備アイテムを Itemyard へ追加、または既存の装備を改修情報で更新します。
		/// 既に同一 ID の装備が存在する場合は Remodel、存在しなければ Add します。
		/// </summary>
		internal static void UpsertSlotItem(Itemyard itemyard, Models.Raw.kcsapi_slotitem raw, string contextForLog)
		{
			if (itemyard == null || raw == null) return;

			try
			{
				if (itemyard.SlotItems.ContainsKey(raw.api_id))
				{
					try { itemyard.SlotItems[raw.api_id].Remodel(raw.api_level, raw.api_slotitem_id); }
					catch (Exception ex) { LogError(contextForLog, ex); }
				}
				else
				{
					try { itemyard.SlotItems.Add(new SlotItem(raw)); }
					catch (Exception ex) { LogError(contextForLog, ex); }
				}
			}
			catch (Exception ex) { LogError(contextForLog, ex); }
		}
	}
}
