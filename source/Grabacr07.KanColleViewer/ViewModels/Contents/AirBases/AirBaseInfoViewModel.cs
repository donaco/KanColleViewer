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

		/// <summary>
		/// 制空値
		/// </summary>
		public int AirPower { get; }

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
			int[] equipmentAlvs,
			int[] equipmentAntiAirs,
			int[] equipmentCounts)
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
					iconType: equipmentIconTypes?[i] ?? "Empty",
					antiAir: equipmentAntiAirs?[i] ?? 0,
					count: equipmentCounts?[i] ?? 0
				);
			}

			this.EquipmentSlots = slots;

			// 制空値を計算
			this.AirPower = CalculateAirPower(slots);
		}

		/// <summary>
		/// 制空値を計算する
		/// </summary>
		private static int CalculateAirPower(EquipmentSlotViewModel[] slots)
		{
			double totalAirPower = 0;

			foreach (var slot in slots)
			{
				if (slot.Count <= 0 || string.IsNullOrEmpty(slot.Name))
					continue;

				// 改修値補正
				double levelBonus = GetLevelBonus(slot.IconType, slot.Level);

				// 内部熟練度
				int internalAlv = GetInternalAlv(slot.Alv);

				// 制空ボーナス
				int airPowerBonus = GetAirPowerBonus(slot.IconType, internalAlv);

				// 制空値 = (対空 + 改修値補正) × √搭載数 + √(内部熟練度/10) + 制空ボーナス
				double slotAirPower = (slot.AntiAir + levelBonus) * Math.Sqrt(slot.Count)
									+ Math.Sqrt(internalAlv / 10.0)
									+ airPowerBonus;

				totalAirPower += Math.Floor(slotAirPower);
			}

			return (int)totalAirPower;
		}

		/// <summary>
		/// 改修値補正を取得する
		/// </summary>
		private static double GetLevelBonus(string iconType, int level)
		{
			if (level <= 0) return 0;

			// 陸上攻撃機、大型陸上機
			if (iconType == "LandBasedAttacker" || iconType == "HeavyBomber")
			{
				// ★1=0.5, ★2=0.7, ... ★10=1.58 (√level × 0.5)
				return Math.Sqrt(level) * 0.5;
			}

			// 艦上戦闘機、陸軍戦闘機、局地戦闘機、水上戦闘機、夜間戦闘機
			if (iconType == "Fighter" || iconType == "NightFighter" ||
				iconType == "SeaplaneFighter" ||
				iconType == "LandBasedFighter" ||
				iconType == "InterceptorFighter" || iconType == "JetInterceptorFighter" || iconType == "AsternInterceptorFighter")
			{
				// ★1=0.2, ★2=0.4, ... ★10=2.0 (level × 0.2)
				return level * 0.2;
			}

			// その他
			return 0;
		}

		/// <summary>
		/// 熟練度(Alv)から内部熟練度を取得する
		/// </summary>
		private static int GetInternalAlv(int alv)
		{
			// 各熟練度の中央値を使用
			switch (alv)
			{
				case 0: return 0;
				case 1: return 10;
				case 2: return 25;
				case 3: return 40;
				case 4: return 55;
				case 5: return 70;
				case 6: return 85;
				case 7: return 100;
				default: return 0;
			}
		}

		/// <summary>
		/// 制空ボーナスを取得する
		/// </summary>
		private static int GetAirPowerBonus(string iconType, int internalAlv)
		{
			// 艦上戦闘機、水上戦闘機、陸軍戦闘機、局地戦闘機
			if (iconType == "Fighter" || iconType == "NightFighter" ||
				iconType == "SeaplaneFighter" ||
				iconType == "LandBasedFighter" ||
				iconType == "InterceptorFighter" || iconType == "JetInterceptorFighter" || iconType == "AsternInterceptorFighter")
			{
				if (internalAlv >= 100) return 22;
				if (internalAlv >= 85) return 14;
				if (internalAlv >= 70) return 14;
				if (internalAlv >= 55) return 9;
				if (internalAlv >= 40) return 5;
				if (internalAlv >= 25) return 2;
				return 0;
			}

			// 水上爆撃機
			if (iconType == "ReconSeaplane" || iconType == "NgihtZuiun")
			{
				if (internalAlv >= 100) return 6;
				if (internalAlv >= 85) return 3;
				if (internalAlv >= 70) return 3;
				if (internalAlv >= 55) return 1;
				if (internalAlv >= 40) return 1;
				if (internalAlv >= 25) return 1;
				return 0;
			}

			// その他
			return 0;
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
		public int AntiAir { get; }
		public int Count { get; }

		/// <summary>
		/// ツールチップ表示用のテキスト
		/// </summary>
		public string ToolTipText { get; }

		/// <summary>
		/// アイコン下部に表示する改修値テキスト
		/// </summary>
		public string LevelText { get; }

		public EquipmentSlotViewModel(int slotId, string name, int level, int alv, string iconType, int antiAir, int count)
		{
			this.SlotId = slotId;
			this.Name = name;
			this.Level = level;
			this.Alv = alv;
			this.IconType = iconType;
			this.AntiAir = antiAir;
			this.Count = count;

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
