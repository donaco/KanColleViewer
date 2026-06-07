using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Grabacr07.KanColleViewer.Models
{
	internal sealed class DaiNaiShipEntry
	{
		public int ShipId { get; set; }
		public bool Daih { get; set; }
		public bool Naik { get; set; }
		public string Name { get; set; }
	}

	internal static class DaiNaiShipProvider
	{
		private static IReadOnlyDictionary<int, DaiNaiShipEntry> _entries;

		public static IReadOnlyDictionary<int, DaiNaiShipEntry> Entries
		{
			get
			{
				if (_entries == null)
					_entries = Load();
				return _entries;
			}
		}

		public static IEnumerable<int> GetShipIds(Func<DaiNaiShipEntry, bool> predicate)
		{
			if (predicate == null)
				return Enumerable.Empty<int>();

			return Entries.Values
				.Where(predicate)
				.Select(x => x.ShipId);
		}

		private static IReadOnlyDictionary<int, DaiNaiShipEntry> Load()
		{
			try
			{
				var dir = Path.GetDirectoryName(
					Assembly.GetEntryAssembly()?.Location
					?? Assembly.GetExecutingAssembly().Location)
					?? AppDomain.CurrentDomain.BaseDirectory;

				var paths = new[]
				{
					Path.Combine(dir, "json", "DaiNai_Ship.json"),
					Path.Combine(dir, "DaiNai_Ship.json"),
				};

				var path = paths.FirstOrDefault(File.Exists);
				if (path == null)
					return new Dictionary<int, DaiNaiShipEntry>();

				var root = JObject.Parse(File.ReadAllText(path));
				var ships = root["DaiNaiShip"] as JObject;
				if (ships == null)
					return new Dictionary<int, DaiNaiShipEntry>();

				var result = new Dictionary<int, DaiNaiShipEntry>();
				foreach (var p in ships.Properties())
				{
					if (!int.TryParse(p.Name, out var id)) continue;
					var obj = p.Value as JObject;
					if (obj == null) continue;

					result[id] = new DaiNaiShipEntry
					{
						ShipId = id,
						Daih = ToBool(obj["Daih"]),
						Naik = ToBool(obj["Naik"]),
						Name = obj["name"]?.Value<string>() ?? ""
					};
				}
				return result;
			}
			catch
			{
				return new Dictionary<int, DaiNaiShipEntry>();
			}
		}

		private static bool ToBool(JToken token)
		{
			if (token == null) return false;
			if (int.TryParse(token.ToString(), out var i)) return i != 0;
			return string.Equals(token.ToString(), "true", StringComparison.OrdinalIgnoreCase);
		}
	}
}
