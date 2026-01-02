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
		
		// 装備アイコンタイプ
		public string[] EquipmentIconTypes { get; set; }
		
		// 装備名
		public string[] EquipmentNames { get; set; }

		// 改修値
		public int[] EquipmentLevels { get; set; }

		// 熟練度
		public int[] EquipmentAlvs { get; set; }
	}
}
