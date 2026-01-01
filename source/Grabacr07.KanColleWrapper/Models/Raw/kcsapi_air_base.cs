using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Models.Raw
{
	// ReSharper disable InconsistentNaming

	/// <summary>
	/// 基地航空隊の情報を表します（api_air_base から取得）
	/// </summary>
	public class kcsapi_air_base
	{
		/// <summary>
		/// 海域ID（6: 中部海域、7: 南西海域、61: 期間限定海域など）
		/// </summary>
		public int api_area_id { get; set; }

		/// <summary>
		/// 航空隊ID（1, 2, 3 など）
		/// </summary>
		public int api_rid { get; set; }

		/// <summary>
		/// 航空隊の名前
		/// </summary>
		public string api_name { get; set; }

		/// <summary>
		/// 距離情報
		/// </summary>
		public ApiDistance api_distance { get; set; }

		/// <summary>
		/// 行動種別（0: 休止、1: 出撃、2: 防空）
		/// </summary>
		public int api_action_kind { get; set; }

		/// <summary>
		/// 航空機情報の配列
		/// </summary>
		public kcsapi_plane_info[] api_plane_info { get; set; }
	}

	/// <summary>
	/// 距離情報を表します
	/// </summary>
	public class ApiDistance
	{
		/// <summary>
		/// 基本距離
		/// </summary>
		public int api_base { get; set; }

		/// <summary>
		/// ボーナス距離
		/// </summary>
		public int api_bonus { get; set; }
	}

	/// <summary>
	/// 航空隊の航空機情報を表します
	/// </summary>
	public class kcsapi_plane_info
	{
		/// <summary>
		/// 航空隊内での航空機スロット番号
		/// </summary>
		public int api_squadron_id { get; set; }

		/// <summary>
		/// 航空機の状態（0: 未配置、1: 配置）
		/// </summary>
		public int api_state { get; set; }

		/// <summary>
		/// 装備スロットID
		/// </summary>
		public int api_slotid { get; set; }

		/// <summary>
		/// 現在の航空機数
		/// </summary>
		public int api_count { get; set; }

		/// <summary>
		/// 最大航空機数
		/// </summary>
		public int api_max_count { get; set; }

		/// <summary>
		/// 航空機の状態値（疲労度など）
		/// </summary>
		public int api_cond { get; set; }
	}

	// ReSharper restore InconsistentNaming
}
