using System.Collections.Generic;
using System.Linq;

// TP計算用のクラスです
namespace Grabacr07.KanColleWrapper.Models
{
	internal static class TransportPointCalculator
	{
		private static readonly IReadOnlyDictionary<int, decimal> ShipTypeTp = new Dictionary<int, decimal>
		{
			{ 2, 5m },   // 駆逐艦
			{ 3, 2m },   // 軽巡洋艦
			{ 10, 7m },  // 航空戦艦
			{ 16, 9m },  // 水上機母艦
			{ 14, 1m },  // 潜水空母
			{ 21, 6m },  // 練習巡洋艦
			{ 6, 4m },   // 航空巡洋艦
			{ 22, 15m }, // 補給艦
			{ 17, 12m }, // 揚陸艦
			{ 20, 7m },  // 潜水母艦
		};

		private static readonly IReadOnlyDictionary<int, decimal> ShipTp = new Dictionary<int, decimal>
		{
			{ 487, 8m }, // 鬼怒改二
		};

		private static readonly IReadOnlyDictionary<int, decimal> SlotItemTp = new Dictionary<int, decimal>
		{
			{ 75, 5m },   // ドラム缶(輸送用)
			{ 68, 8m },   // 大発動艇
			{ 193, 8m },  // 特大発動艇
			{ 166, 8m },  // 大発動艇(八九式中戦車＆陸戦隊)
			{ 230, 8m },  // 特大発動艇＋戦車第11連隊
			{ 449, 8m },  // 特大発動艇+一式砲戦車
			{ 355, 8m },  // M4A1 DD
			{ 436, 8m },  // 大発動艇(II号戦車/北アフリカ仕様)
			{ 482, 8m },  // 特大発動艇+Ⅲ号戦車(北アフリカ仕様)
			{ 408, 8m },  // 装甲艇(AB艇)
			{ 409, 8m },  // 武装大発
			{ 494, 8m },  // 特大発動艇+チハ
			{ 495, 8m },  // 特大発動艇+チハ改
			{ 514, 8m },  // 特大発動艇+Ⅲ号戦車J型
			{ 167, 2m },  // 特二式内火艇
			{ 525, 2m },  // 特四式内火艇
			{ 526, 2m },  // 特四式内火艇改
			{ 145, 1m },  // 戦闘糧食
			{ 150, 1m },  // 秋刀魚の缶詰
			{ 241, 1m },  // 戦闘糧食(特別なおにぎり)
		};

		public static decimal Calculate(IEnumerable<Ship> ships)
		{
			if (ships == null) return 0m;

			var shipArray = ships.Where(x => x != null).ToArray();

			var shipTp = shipArray.Sum(GetShipTp);
			var slotTp = shipArray.Sum(s => s.EquippedItems?.Sum(slot => GetSlotItemTp(slot.Item?.Info?.Id ?? 0)) ?? 0m);

			return shipTp + slotTp;
		}

		private static decimal GetShipTp(Ship ship)
		{
			var shipInfo = ship?.Info;
			if (shipInfo == null || shipInfo == ShipInfo.Dummy) return 0m;

			if (ShipTp.TryGetValue(shipInfo.Id, out var uniqueTp))
			{
				return uniqueTp;
			}

			var stypeId = shipInfo.ShipType?.Id ?? 0;
			return ShipTypeTp.TryGetValue(stypeId, out var stypeTp) ? stypeTp : 0m;
		}

		private static decimal GetSlotItemTp(int slotItemMasterId)
		{
			return SlotItemTp.TryGetValue(slotItemMasterId, out var tp) ? tp : 0m;
		}
	}
}
