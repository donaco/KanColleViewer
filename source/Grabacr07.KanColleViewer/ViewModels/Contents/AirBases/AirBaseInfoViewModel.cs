using System;
using System.Linq;
using Grabacr07.KanColleWrapper.Models;
using System.Diagnostics;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.AirBases
{
	/// <summary>
	/// 制空値計算のヘルパークラス
	/// </summary>
	internal static class AirPowerCalculator
	{
		// SlotItemType の値を可読性のために定義
		private const int 艦上戦闘機 = 6;
		private const int 艦上爆撃機 = 7;
		private const int 艦上攻撃機 = 8;
		private const int 艦上偵察機 = 9;
		private const int 水上偵察機 = 10;
		private const int 水上爆撃機 = 11;
		private const int 回転翼機 = 25;
		private const int 対潜哨戒機 = 26;
		private const int 水上戦闘機 = 45;
		private const int 陸上攻撃機 = 47;
		private const int 局地戦闘機 = 48;
		private const int 陸上偵察機 = 49;
		private const int 大型陸上機 = 53;
		private const int 噴式戦闘機 = 56;
		private const int 噴式戦闘爆撃機 = 57;
		private const int 噴式攻撃機 = 58;
		private const int 噴式偵察機 = 59;


		// 偵察機ボーナス対象(出撃)の装備ID (slotItemId)
		// 1.18倍: 二式陸偵(熟練)系
		private static readonly int[] ReconBonuss118 = { 312, 480 };
		// 1.15倍: 二式陸偵
		private static readonly int[] ReconBonuss115 = { 311 };


		// 偵察機ボーナス対象(防空)の装備ID (slotItemId)
		// 1.3倍: 彩雲系
		private static readonly int[] ReconBonus130 = { 54, 151, 212, 273 };
		// 1.24倍: 二式陸偵(熟練)系
		private static readonly int[] ReconBonus124 = { 312, 480 };
		// 1.2倍: 二式艦偵系
		private static readonly int[] ReconBonus120 = { 61, 423, 543 };
		// 1.18倍: 二式陸偵
		private static readonly int[] ReconBonus118 = { 311 };

		/// <summary>
		/// スロット制空値を計算する
		/// 出撃時: 制空値 = (対空 + (改修補正 × 改修値) + (1.5 × 迎撃値)) × √搭載数 + 熟練度補正
		/// 防空時: 制空値 = (対空 + (改修補正 × 改修値) + 迎撃値 + (2 × 対爆値)) × √搭載数 + 熟練度補正
		/// ※ 局地戦闘機のみ迎撃/対爆補正の計算式が異なる
		/// </summary>
		public static int CalculateSlotAirPower(int slotItemType, int antiAir, int level, int alv, int count, int intercept, int antibomb, int actionKind)
		{
			if (count <= 0) return 0;

			double levelCoefficient = GetLevelCoefficient(slotItemType);
			int internalAlv = GetInternalAlv(alv);
			int proficiencyBonus = GetProficiencyBonus(slotItemType, internalAlv);

			// 迎撃/対爆補正の計算
			double interceptBonus = GetInterceptBonus(slotItemType, intercept, antibomb, actionKind);

			double slotAirPower = (antiAir + (levelCoefficient * level) + interceptBonus) * Math.Sqrt(count)
								+ proficiencyBonus;

			return (int)Math.Floor(slotAirPower);
		}

		/// <summary>
		/// 出撃時の偵察機ボーナス倍率を取得する
		/// 装備ID (slotItemId) に基づいて倍率を決定
		/// </summary>
		public static double GetReconBonuss(int[] slotItemIds, int actionKind)
		{
			// 出撃時 (actionKind == 1) のみ適用
			if (actionKind != 1) return 1.0;
			if (slotItemIds == null || slotItemIds.Length == 0) return 1.0;

			// 最も高いボーナス倍率を返す（複数の偵察機がある場合）
			double maxBonus = 1.0;

			foreach (var slotItemId in slotItemIds)
			{
				if (ReconBonuss118.Contains(slotItemId))
				{
					maxBonus = Math.Max(maxBonus, 1.18);
				}
				else if (ReconBonuss115.Contains(slotItemId))
				{
					maxBonus = Math.Max(maxBonus, 1.15);
				}
			}

			return maxBonus;
		}


		/// <summary>
		/// 防空時の偵察機ボーナス倍率を取得する
		/// 装備ID (slotItemId) に基づいて倍率を決定
		/// </summary>
		public static double GetReconBonus(int[] slotItemIds, int actionKind)
		{
			// 防空時 (actionKind == 2) のみ適用
			if (actionKind != 2) return 1.0;
			if (slotItemIds == null || slotItemIds.Length == 0) return 1.0;

			// 最も高いボーナス倍率を返す（複数の偵察機がある場合）
			double maxBonus = 1.0;

			foreach (var slotItemId in slotItemIds)
			{
				if (ReconBonus130.Contains(slotItemId))
				{
					maxBonus = Math.Max(maxBonus, 1.3);
				}
				else if (ReconBonus124.Contains(slotItemId))
				{
					maxBonus = Math.Max(maxBonus, 1.24);
				}
				else if (ReconBonus120.Contains(slotItemId))
				{
					maxBonus = Math.Max(maxBonus, 1.2);
				}
				else if (ReconBonus118.Contains(slotItemId))
				{
					maxBonus = Math.Max(maxBonus, 1.18);
				}
			}

			return maxBonus;
		}

		/// <summary>
		/// 迎撃/対爆補正を取得する
		/// </summary>
		private static double GetInterceptBonus(int slotItemType, int intercept, int antibomb, int actionKind)
		{
			// 局地戦闘機 の場合のみ特別計算
			if (slotItemType == 局地戦闘機)
			{
				if (actionKind == 2) // 防空
				{
					// 防空時: 迎撃値 + 2 × 対爆値
					return intercept + (2.0 * antibomb);
				}
				else // 出撃 (actionKind == 1) およびその他
				{
					// 出撃時: 1.5 × 迎撃値
					return 1.5 * intercept;
				}
			}

			// その他の機種は従来通り 1.5 × 迎撃値
			return 1.5 * intercept;
		}

		/// <summary>
		/// 改修補正係数を取得する
		/// </summary>
		public static double GetLevelCoefficient(int slotItemType)
		{
			switch (slotItemType)
			{
				// 陸攻系: 0.5
				case 陸上攻撃機:
				case 大型陸上機:
					return 0.5;

				// 戦闘機系: 0.2
				case 艦上戦闘機:
				case 水上戦闘機:
				case 局地戦闘機:
				case 噴式戦闘機:
					return 0.2;

				default:
					return 0;
			}
		}

		/// <summary>
		/// 内部熟練度を取得する
		/// </summary>
		public static int GetInternalAlv(int alv)
		{
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
		/// 熟練度ボーナスを取得する
		/// </summary>
		public static int GetProficiencyBonus(int slotItemType, int internalAlv)
		{
			switch (slotItemType)
			{
				// 戦闘機系: 最大+22
				case 艦上戦闘機:
				case 水上戦闘機:
				case 局地戦闘機:
				case 噴式戦闘機:
				case 対潜哨戒機:
					if (internalAlv >= 100) return 22;
					if (internalAlv >= 85) return 14;
					if (internalAlv >= 70) return 14;
					if (internalAlv >= 55) return 9;
					if (internalAlv >= 40) return 5;
					if (internalAlv >= 25) return 2;
					return 0;

				// 水偵系: 最大+6
				case 水上偵察機:
				case 水上爆撃機:
					if (internalAlv >= 100) return 6;
					if (internalAlv >= 85) return 3;
					if (internalAlv >= 70) return 3;
					if (internalAlv >= 55) return 1;
					if (internalAlv >= 40) return 1;
					if (internalAlv >= 25) return 1;
					return 0;

				default:
					return 0;
			}
		}
	}

	public class AirBaseInfoViewModel
	{
		public string Name { get; }
		public int ActionKind { get; }
		public int Distance { get; }
		public int[] EquipmentSlotIds { get; }
		public string[] EquipmentIconTypes { get; }
		public int[] EquipmentTypes { get; }
		public int[] EquipmentSlotItemIds { get; }
		public EquipmentSlotViewModel[] EquipmentSlots { get; }
		public int AirPower { get; }
		public double ReconBonus { get; }

		/// <summary>
		/// 装備スロットの中で最も高い Cond 値を取得
		/// </summary>
		public int MaxCond { get; }

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
			int[] equipmentTypes,
			int[] equipmentSlotItemIds,
			string[] equipmentNames,
			int[] equipmentLevels,
			int[] equipmentAlvs,
			int[] equipmentAntiAirs,
			int[] equipmentIntercepts,
			int[] equipmentAntibombs,
			int[] equipmentCounts,
			int[] equipmentMaxCounts,
			int[] equipmentConds)
		{
			this.Name = name;
			this.ActionKind = actionKind;
			this.Distance = distance;
			this.EquipmentSlotIds = equipmentSlotIds ?? new int[0];
			this.EquipmentIconTypes = equipmentIconTypes ?? new string[0];
			this.EquipmentTypes = equipmentTypes ?? new int[0];
			this.EquipmentSlotItemIds = equipmentSlotItemIds ?? new int[0];

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
					slotItemType: equipmentTypes?[i] ?? 0,
					antiAir: equipmentAntiAirs?[i] ?? 0,
					intercept: equipmentIntercepts?[i] ?? 0,
					antibomb: equipmentAntibombs?[i] ?? 0,
					count: equipmentCounts?[i] ?? 0,
					maxCount: equipmentMaxCounts?[i] ?? 0,
					cond: equipmentConds?[i] ?? 0,
					actionKind: actionKind
				);
			}

			this.EquipmentSlots = slots;

			// 最大 Cond 値を計算（装備がある場合のみ）
			this.MaxCond = slots.Length > 0 ? slots.Max(s => s.Cond) : 0;

			// 出撃時のボーナス（今後の拡張用、現在は出撃時ボーナスがない場合）
			double sortieBonus = AirPowerCalculator.GetReconBonus(this.EquipmentSlotItemIds, actionKind);

			// 偵察機ボーナス倍率を取得（装備IDベース）
			this.ReconBonus = AirPowerCalculator.GetReconBonus(this.EquipmentSlotItemIds, actionKind);

			// 各スロットの制空値合計に偵察機ボーナスを適用
			int baseAirPower = slots.Sum(s => s.SlotAirPower);
			this.AirPower = (int)Math.Floor(baseAirPower * this.ReconBonus);
		}
	}

	public class EquipmentSlotViewModel
	{
		public int SlotId { get; }
		public string Name { get; }
		public int Level { get; }
		public int Alv { get; }
		public string IconType { get; }
		public int SlotItemType { get; }
		public int AntiAir { get; }
		public int Intercept { get; }
		public int Antibomb { get; }
		public int Count { get; }
		public int MaxCount { get; }
		public int Cond { get; }
		public string ToolTipText { get; }
		public string LevelText { get; }
		public int SlotAirPower { get; }

		public EquipmentSlotViewModel(int slotId, string name, int level, int alv, string iconType, int slotItemType, int antiAir, int intercept, int antibomb, int count, int maxCount, int cond, int actionKind)
		{
			this.SlotId = slotId;
			this.Name = name;
			this.Level = level;
			this.Alv = alv;
			this.IconType = iconType;
			this.SlotItemType = slotItemType;
			this.AntiAir = antiAir;
			this.Intercept = intercept;
			this.Antibomb = antibomb;
			this.Count = count;
			this.MaxCount = maxCount;
			this.Cond = cond;

			this.LevelText = level >= 10 ? "★max" : level > 0 ? $"★+{level}" : "";
			this.SlotAirPower = AirPowerCalculator.CalculateSlotAirPower(slotItemType, antiAir, level, alv, count, intercept, antibomb, actionKind);

			if (string.IsNullOrEmpty(name))
			{
				this.ToolTipText = "";
			}
			else
			{
				var tooltip = name;
				if (level > 0) tooltip += level >= 10 ? " ★max" : $" ★+{level}";
				if (alv > 0) tooltip += $" (熟練度{alv})";
				if (intercept > 0) tooltip += $" 迎撃:{intercept}";
				if (antibomb > 0) tooltip += $" 対爆:{antibomb}";
				if (this.SlotAirPower > 0) tooltip += $" 制空:{this.SlotAirPower}";
				this.ToolTipText = tooltip;
			}
		}
	}
}
