using System;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.AirBases
{
	public class AirBaseInfoViewModel
	{
		public string Name { get; }
		public int ActionKind { get; }
		public int Distance { get; }
		public int[] EquipmentSlotIds { get; }
		public string[] EquipmentIconTypes { get; }

		public string ActionKindText
		{
			get
			{
				switch (this.ActionKind)
				{
					case 1: return "出撃";
					case 2: return "防空";
					case 3: return "退避";
					case 4: return "休息";
					case 0: return "待機";
					default: return "不明";
				}
			}
		}

		public AirBaseInfoViewModel(string name, int actionKind, int distance, int[] equipmentSlotIds, string[] equipmentIconTypes)
		{
			this.Name = name;
			this.ActionKind = actionKind;
			this.Distance = distance;
			this.EquipmentSlotIds = equipmentSlotIds ?? new int[0];
			this.EquipmentIconTypes = equipmentIconTypes ?? new string[0];
		}
	}
}
