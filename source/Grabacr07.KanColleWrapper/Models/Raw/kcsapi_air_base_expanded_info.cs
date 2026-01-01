using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Models.Raw
{
	// ReSharper disable InconsistentNaming

	/// <summary>
	/// 航空隊の拡張情報を表します（api_air_base_expanded_info から取得）
	/// </summary>
	public class kcsapi_air_base_expanded_info
	{
		/// <summary>
		/// 海域ID（6: 中部海域、7: 南西海域、61: 期間限定海域など）
		/// </summary>
		public int api_area_id { get; set; }

		/// <summary>
		/// 整備レベル
		/// </summary>
		public int api_maintenance_level { get; set; }
	}

	// ReSharper restore InconsistentNaming
}
