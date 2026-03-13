using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// マップセルの1件分の情報を保持します。
	/// </summary>
	public class CellInfo
	{
		public string Name { get; }
		public bool IsBoss { get; }

		public CellInfo(string name, bool isBoss)
		{
			this.Name = name;
			this.IsBoss = isBoss;
		}
	}

	/// <summary>
	/// マップセルの名称を提供するプロバイダーです。
	/// 海域ごとに異なるセル名マッピングに対応しています。
	/// </summary>
	public static class MapCellNameProvider
	{
		/// <summary>
		/// マップID（海域-マップ番号）をキーとした、セル番号→CellInfo のマッピング
		/// </summary>
		private static Dictionary<string, Dictionary<int, CellInfo>> _cellInfoByMap;
		private static string _jsonFilePath;
		private static DateTime _lastLoadTime = DateTime.MinValue;

		static MapCellNameProvider()
		{
			// exe と同じディレクトリの MapCellNames.json を指定
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;
			var executableDir = Path.GetDirectoryName(executablePath);
			_jsonFilePath = Path.Combine(executableDir, "MapCellNames.json");

			_cellInfoByMap = LoadCellNames();
		}

		/// <summary>
		/// セル番号を文字表記に変換します。
		/// ボス情報は含まず、純粋なセル名のみ返します（例: "C"）。
		/// </summary>
		/// <param name="mapAreaId">海域ID（例: 1, 7）</param>
		/// <param name="mapInfoNo">マップ番号（例: 5, 2）</param>
		/// <param name="cellNo">セル番号</param>
		/// <returns>変換後の表記（例: "G"）。変換不可の場合は元の番号を文字列で返す</returns>
		public static string GetCellName(int mapAreaId, int mapInfoNo, int cellNo)
		{
			var info = GetCellInfo(mapAreaId, mapInfoNo, cellNo);
			return info != null && !string.IsNullOrEmpty(info.Name)
				? info.Name
				: cellNo.ToString();
		}

		/// <summary>
		/// セル番号に対応する CellInfo を取得します。
		/// </summary>
		public static CellInfo GetCellInfo(int mapAreaId, int mapInfoNo, int cellNo)
		{
			TryReloadIfModified();

			var mapKey = $"{mapAreaId}-{mapInfoNo}";

			if (_cellInfoByMap.TryGetValue(mapKey, out var cellInfos))
			{
				if (cellInfos.TryGetValue(cellNo, out var info))
				{
					return info;
				}
			}

			return null;
		}

		/// <summary>
		/// 指定したセルがボスセルかどうかを判定します。
		/// MapCellNames.json の boss フラグに基づいて判定します。
		/// </summary>
		/// <param name="mapAreaId">海域ID（例: 7）</param>
		/// <param name="mapInfoNo">マップ番号（例: 4）</param>
		/// <param name="cellNo">セル番号</param>
		/// <returns>ボスセルであれば true</returns>
		public static bool IsBossCell(int mapAreaId, int mapInfoNo, int cellNo)
		{
			var info = GetCellInfo(mapAreaId, mapInfoNo, cellNo);
			return info != null && info.IsBoss;
		}

		/// <summary>
		/// JSON ファイルが変更されていれば再読み込みします。
		/// </summary>
		private static void TryReloadIfModified()
		{
			try
			{
				if (!File.Exists(_jsonFilePath))
					return;

				var fileInfo = new FileInfo(_jsonFilePath);
				var lastWriteTime = fileInfo.LastWriteTime;

				if (lastWriteTime > _lastLoadTime)
				{
					_cellInfoByMap = LoadCellNames();
				}
			}
			catch
			{
			}
		}

		/// <summary>
		/// JSON ファイルからセル名を読み込みます。
		/// 文字列値 → CellInfo(name, boss: false)
		/// オブジェクト値 → CellInfo(name, boss) として読み込みます。
		/// </summary>
		private static Dictionary<string, Dictionary<int, CellInfo>> LoadCellNames()
		{
			var result = new Dictionary<string, Dictionary<int, CellInfo>>();
			_lastLoadTime = DateTime.Now;

			try
			{

				if (!File.Exists(_jsonFilePath))
				{
					return result;
				}

				var json = File.ReadAllText(_jsonFilePath);
				var root = JObject.Parse(json);
				var maps = root["maps"] as JObject;

				if (maps != null)
				{
					foreach (var mapProp in maps.Properties())
					{
						var mapKey = mapProp.Name;
						var cellNamesObj = mapProp.Value as JObject;

						if (cellNamesObj != null)
						{
							var cellInfos = new Dictionary<int, CellInfo>();

							foreach (var cellProp in cellNamesObj.Properties())
							{
								if (int.TryParse(cellProp.Name, out var cellNo))
								{
									CellInfo info;

									if (cellProp.Value.Type == JTokenType.Object)
									{
										// オブジェクト形式: { "name": "C", "boss": true }
										var obj = cellProp.Value as JObject;
										var name = obj?["name"]?.Value<string>() ?? "";
										var boss = obj?["boss"]?.Value<bool>() ?? false;
										info = new CellInfo(name, boss);
									}
									else
									{
										// 文字列形式（従来互換）: "A"
										var name = cellProp.Value?.Value<string>() ?? "";
										info = new CellInfo(name, false);
									}

									cellInfos[cellNo] = info;
								}
							}

							result[mapKey] = cellInfos;
						}
					}
				}

			}
			catch
			{
			}

			return result;
		}
	}
}
