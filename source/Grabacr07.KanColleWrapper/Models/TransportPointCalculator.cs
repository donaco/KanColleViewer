using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

// TP計算用のクラスです
namespace Grabacr07.KanColleWrapper.Models
{
	internal static class TransportPointCalculator
	{
		private static Dictionary<int, decimal> _shipTypeTp;
		private static Dictionary<int, decimal> _shipTp;
		private static Dictionary<int, decimal> _ItemTp;

		private static string _jsonFilePath;
		private static DateTime _lastLoadTime = DateTime.MinValue;

		static TransportPointCalculator()
		{
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;
			var executableDir = Path.GetDirectoryName(executablePath)
				?? AppDomain.CurrentDomain.BaseDirectory;

			_jsonFilePath = Path.Combine(executableDir, "json", "TP_Ship_SlotItem.json");
			LoadTpSettings();
		}

		public static decimal Calculate(IEnumerable<Ship> ships)
		{
			TryReloadIfModified();

			if (ships == null) return 0m;

			var shipArray = ships.Where(x => x != null).ToArray();

			var shipTp = shipArray.Sum(GetShipTp);
			var itemTp = shipArray.Sum(s => s.EquippedItems?.Sum(slot => GetItemTp(slot.Item?.Info?.Id ?? 0)) ?? 0m);

			return shipTp + itemTp;
		}

		private static decimal GetShipTp(Ship ship)
		{
			var shipInfo = ship?.Info;
			if (shipInfo == null || shipInfo == ShipInfo.Dummy) return 0m;

			if (_shipTp.TryGetValue(shipInfo.Id, out var uniqueTp))
			{
				return uniqueTp;
			}

			var stypeId = shipInfo.ShipType?.Id ?? 0;
			return _shipTypeTp.TryGetValue(stypeId, out var stypeTp) ? stypeTp : 0m;
		}

		private static decimal GetItemTp(int itemMasterId)
		{
			return _ItemTp.TryGetValue(itemMasterId, out var tp) ? tp : 0m;
		}

		private static void TryReloadIfModified()
		{
			try
			{
				if (!File.Exists(_jsonFilePath))
					return;

				var fileInfo = new FileInfo(_jsonFilePath);
				if (fileInfo.LastWriteTime > _lastLoadTime)
				{
					LoadTpSettings();
				}
			}
			catch
			{
			}
		}

		private static void LoadTpSettings()
		{
			// フォールバック（現行ハードコード値）
			_shipTypeTp = new Dictionary<int, decimal>
			{
				{ 2, 5m }, { 3, 2m }, { 10, 7m }, { 16, 9m }, { 14, 1m },
				{ 21, 6m }, { 6, 4m }, { 22, 15m }, { 17, 12m }, { 20, 7m },
			};

			_shipTp = new Dictionary<int, decimal>
			{
				{ 487, 8m },
			};

			_ItemTp = new Dictionary<int, decimal>
			{
				{ 75, 5m }, { 68, 8m }, { 193, 8m }, { 166, 8m }, { 230, 8m },
				{ 449, 8m }, { 355, 8m }, { 436, 8m }, { 482, 8m }, { 408, 8m },
				{ 409, 8m }, { 494, 8m }, { 495, 8m }, { 514, 8m }, { 167, 2m },
				{ 525, 2m }, { 526, 2m }, { 145, 1m }, { 150, 1m }, { 241, 1m },
			};

			_lastLoadTime = DateTime.Now;

			try
			{
				if (!File.Exists(_jsonFilePath))
				{
					return;
				}

				var json = File.ReadAllText(_jsonFilePath);
				var root = JObject.Parse(json);

				_shipTypeTp = ParseDictionary(root["shipTypeTp"] as JObject, "shipTypeTp") ?? _shipTypeTp;
				_shipTp = ParseDictionary(root["shipTp"] as JObject, "shipTp") ?? _shipTp;
				_ItemTp = ParseDictionary(root["ItemTp"] as JObject, "ItemTp") ?? _ItemTp;
			}
			catch
			{
				// 読み込み失敗時はフォールバック値を使用
			}
		}

		private static Dictionary<int, decimal> ParseDictionary(JObject obj, string valueKey)
		{
			if (obj == null) return null;

			var result = new Dictionary<int, decimal>();
			foreach (var p in obj.Properties())
			{
				if (!int.TryParse(p.Name, out var id)) continue;

				string s;
				if (p.Value is JObject o)
				{
					s = o[valueKey]?.ToString(); // 例: shipTypeTp / shipTp / ItemTp
				}
				else
				{
					s = p.Value?.ToString(); // 旧形式(数値直書き)との互換
				}

				if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ||
					decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out v))
				{
					result[id] = v;
				}
			}
			return result;
		}
	}
}
