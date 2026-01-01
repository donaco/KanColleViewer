using System;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// 個別基地情報（名前・出撃状態・行動半径・装備・装備アイコン）
	/// </summary>
	public class AirBaseInfo
	{
		public string Name { get; set; }
		public int ActionKind { get; set; }
		public int Distance { get; set; }
		public int[] EquipmentSlotIds { get; set; }
		public string[] EquipmentIconTypes { get; set; }
	}
}
