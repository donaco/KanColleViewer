using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Models.Raw
{
	// ReSharper disable InconsistentNaming

	/// <summary>
	/// マップ情報を表します
	/// </summary>
	public class kcsapi_mapinfo
	{
		/// <summary>
		/// マップID
		/// </summary>
		public int api_id { get; set; }

		/// <summary>
		/// クリア済みフラグ
		/// </summary>
		public int api_cleared { get; set; }

		/// <summary>
		/// 撃破数（ボスゲージ用）
		/// </summary>
		public int api_defeat_count { get; set; }

		/// <summary>
		/// 必要撃破数
		/// </summary>
		public int api_required_defeat_count { get; set; }

		/// <summary>
		/// ゲージタイプ
		/// </summary>
		public int api_gauge_type { get; set; }

		/// <summary>
		/// ゲージ番号
		/// </summary>
		public int api_gauge_num { get; set; }

		/// <summary>
		/// 航空隊デッキ数（このマップで使用できる航空隊基地の数）
		/// </summary>
		public int api_air_base_decks { get; set; }
	}

	// ReSharper restore InconsistentNaming
}
