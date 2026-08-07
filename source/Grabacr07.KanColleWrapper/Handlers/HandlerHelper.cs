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
			var dict = new Dictionary<string, string>();
			if (string.IsNullOrEmpty(requestBody)) return dict;
			foreach (var p in requestBody.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var kv = p.Split(new[] { '=' }, 2);
				if (kv.Length == 2)
					dict[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
			}
			return dict;
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
	}
}
