using System;
using System.Linq;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.AirBases
{
	public class AirBaseInfoViewModel
	{
		public string Name { get; }
		public int ActionKind { get; }
		public int Distance { get; }
		public int[] EquipmentSlotIds { get; }
		public string[] EquipmentIconTypes { get; }

		/// <summary>
		/// 装備スロット情報（ツールチップ表示用）
		/// </summary>
		public EquipmentSlotViewModel[] EquipmentSlots { get; }

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

		public AirBaseInfoViewModel(
			string name,
			int actionKind,
			int distance,
			int[] equipmentSlotIds,
			string[] equipmentIconTypes,
			string[] equipmentNames,
			int[] equipmentLevels,
			int[] equipmentAlvs)
		{
			this.Name = name;
			this.ActionKind = actionKind;
			this.Distance = distance;
			this.EquipmentSlotIds = equipmentSlotIds ?? new int[0];
			this.EquipmentIconTypes = equipmentIconTypes ?? new string[0];

			// 装備スロット情報を構築（空のスロットも含める）
			var slotCount = equipmentSlotIds?.Length ?? 0;
			var slots = new EquipmentSlotViewModel[slotCount];

			for (int i = 0; i < slotCount; i++)
			{
				slots[i] = new EquipmentSlotViewModel(
					slotId: equipmentSlotIds?[i] ?? 0,
					name: equipmentNames?[i] ?? "",
					level: equipmentLevels?[i] ?? 0,
					alv: equipmentAlvs?[i] ?? 0,
					iconType: equipmentIconTypes?[i] ?? "Empty"  // 空きスロットは "Empty"
				);
			}

			this.EquipmentSlots = slots;
		}
	}

	/// <summary>
	/// 装備スロット情報（ツールチップ表示用）
	/// </summary>
	public class EquipmentSlotViewModel
	{
		public int SlotId { get; }
		public string Name { get; }
		public int Level { get; }
		public int Alv { get; }
		public string IconType { get; }

		/// <summary>
		/// ツールチップ表示用のテキスト
		/// 例: "彩雲 ★+2 (熟練度7)"
		/// </summary>
		public string ToolTipText { get; }

		/// <summary>
		/// アイコン下部に表示する改修値テキスト
		/// 例: "★+2" または "★MAX"
		/// </summary>
		public string LevelText { get; }

		public EquipmentSlotViewModel(int slotId, string name, int level, int alv, string iconType)
		{
			this.SlotId = slotId;
			this.Name = name;
			this.Level = level;
			this.Alv = alv;
			this.IconType = iconType;

			// 改修値テキストを構築
			if (level >= 10)
			{
				this.LevelText = "★max";
			}
			else if (level > 0)
			{
				this.LevelText = $"★+{level}";
			}
			else
			{
				this.LevelText = "";
			}

			// ツールチップテキストを構築
			if (string.IsNullOrEmpty(name))
			{
				this.ToolTipText = "";
			}
			else
			{
				var tooltip = name;

				// 改修値を追加
				if (level > 0)
				{
					if (level >= 10)
					{
						tooltip += " ★max";
					}
					else
					{
						tooltip += $" ★+{level}";
					}
				}

				// 熟練度を追加
				if (alv > 0)
				{
					tooltip += $" (熟練度{alv})";
				}

				this.ToolTipText = tooltip;
			}
		}
	}
}
