// ReSharper disable InconsistentNaming

namespace Grabacr07.KanColleWrapper.Models
{
	public enum SlotItemType
	{
		小口径主砲 = 1,
		中口径主砲 = 2,
		大口径主砲 = 3,
		副砲 = 4,
		魚雷 = 5,
		艦上戦闘機 = 6,
		艦上爆撃機 = 7,
		艦上攻撃機 = 8,
		艦上偵察機 = 9,
		水上偵察機 = 10,
		水上爆撃機 = 11,
		小型電探 = 12,
		大型電探 = 13,
		ソナー = 14,
		爆雷 = 15,
		増設バルジ_ = 16,
		機関部強化 = 17,
		三式弾 = 18,
		徹甲弾 = 19,
		VT信管 = 20,
		対空機銃 = 21,
		甲標的 = 22,
		応急修理要員 = 23,
		大発動艇 = 24,
		回転翼機 = 25,
		対潜哨戒機 = 26,
		増設バルジ = 27,
		大型バルジ = 28,
		探照灯 = 29,
		ドラム缶 = 30,
		艦艇修理施設 = 31,
		潜水艦魚雷 = 32,
		照明弾 = 33,
		司令部施設 = 34,
		航空要員 = 35,
		高射装置 = 36,
		対地装備 = 37,
		大口径主砲_II = 38,
		水上艦要員 = 39,
		大型ソナー = 40,
		大型飛行艇 = 41,
		大型探照灯 = 42,
		戦闘糧食 = 43,
		洋上補給 = 44,
		水上戦闘機 = 45,
		内火艇 = 46,
		陸上攻撃機 = 47,
		局地戦闘機 = 48,
		陸上偵察機 = 49,
		輸送機材 = 50,
		潜水艦装備 = 51,
		陸戦部隊 = 52,
		大型陸上機 = 53,
		水上艦装備 = 54,
		噴式戦闘機 = 56,
		噴式戦闘爆撃機 = 57,
		噴式攻撃機 = 58,
		噴式偵察機 = 59,
		噴式戦闘爆撃機II = 91,
		大型電探_II = 93,
		艦上偵察機_II = 94,
		副砲II = 95,
	}

	public static class SlotItemTypeExtensions
	{
		public static bool IsNumerable(this SlotItemType type)
		{
			switch (type)
			{
				case SlotItemType.艦上偵察機:
				case SlotItemType.艦上偵察機_II:
				case SlotItemType.艦上戦闘機:
				case SlotItemType.艦上攻撃機:
				case SlotItemType.艦上爆撃機:
				case SlotItemType.水上偵察機:
				case SlotItemType.水上爆撃機:
				case SlotItemType.水上戦闘機:
				case SlotItemType.回転翼機:
				case SlotItemType.対潜哨戒機:
				case SlotItemType.大型飛行艇:
				case SlotItemType.噴式戦闘機:
				case SlotItemType.噴式戦闘爆撃機:
				case SlotItemType.噴式戦闘爆撃機II:
				case SlotItemType.噴式攻撃機:
				case SlotItemType.噴式偵察機:
					return true;

				default:
					return false;
			}
		}
	}
}
