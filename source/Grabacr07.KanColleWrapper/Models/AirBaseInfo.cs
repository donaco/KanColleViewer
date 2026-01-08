using System;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// 個別基地情報
	/// </summary>
	public class AirBaseInfo
	{
		// 航空隊名
		public string Name { get; set; }

		// 出撃状態(出撃・退避　等)
		public int ActionKind { get; set; }

		// 行動半径
		public int Distance { get; set; }

		// 装備ID
		public int[] EquipmentSlotIds { get; set; }

		// 装備マスターID (api_slotitem_id)
		public int[] EquipmentSlotItemIds { get; set; }

		// 装備 Cond 値 (api_cond)
		public int[] EquipmentConds { get; set; }

		// 装備アイコンタイプ (表示用、従来互換)
		public string[] EquipmentIconTypes { get; set; }

		// 装備種別 (SlotItemType の int 値)
		public int[] EquipmentTypes { get; set; }

		// 装備名
		public string[] EquipmentNames { get; set; }

		// 改修値
		public int[] EquipmentLevels { get; set; }

		// 熟練度
		public int[] EquipmentAlvs { get; set; }

		// 対空
		public int[] EquipmentAntiAirs { get; set; }

		// 迎撃値 (api_houm)
		public int[] EquipmentIntercepts { get; set; }

		// 対爆値 (api_houk)
		public int[] EquipmentAntibombs { get; set; }

		// 現在搭載数 (api_count)
		public int[] EquipmentCounts { get; set; }

		// 最大搭載数 (api_max_count)
		public int[] EquipmentMaxCounts { get; set; }
	}
}
