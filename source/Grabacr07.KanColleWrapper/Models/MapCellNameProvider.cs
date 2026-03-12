using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// マップセルの名称を提供するプロバイダーです。
	/// 海域ごとに異なるセル名マッピングに対応しています。
	/// </summary>
	public static class MapCellNameProvider
	{
		/// <summary>
		/// マップID（海域-マップ番号）をキーとした、セル番号→名称のマッピング
		/// </summary>
		private static Dictionary<string, Dictionary<int, string>> _cellNamesByMap;
		private static string _jsonFilePath;
		private static DateTime _lastLoadTime = DateTime.MinValue;

		static MapCellNameProvider()
		{
			// exe と同じディレクトリの MapCellNames.json を指定
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;
			var executableDir = Path.GetDirectoryName(executablePath);
			_jsonFilePath = Path.Combine(executableDir, "MapCellNames.json");

			_cellNamesByMap = LoadCellNames();
		}

		/// <summary>
		/// セル番号を文字表記に変換します。
		/// </summary>
		/// <param name="mapAreaId">海域ID（例: 1, 7）</param>
		/// <param name="mapInfoNo">マップ番号（例: 5, 2）</param>
		/// <param name="cellNo">セル番号</param>
		/// <returns>変換後の表記（例: "G", "O (BOSS)"）。変換不可の場合は元の番号を文字列で返す</returns>
		public static string GetCellName(int mapAreaId, int mapInfoNo, int cellNo)
		{
			// ファイルが更新されている場合は再読み込み（開発時の JSON 変更に対応）
			TryReloadIfModified();

			var mapKey = $"{mapAreaId}-{mapInfoNo}";

			if (_cellNamesByMap.TryGetValue(mapKey, out var cellNames))
			{
				if (cellNames.TryGetValue(cellNo, out var name))
				{
					// 空文字列の場合は番号を返す
					return string.IsNullOrEmpty(name) ? cellNo.ToString() : name;
				}
			}

			// マップに無い場合は番号を返す
			return cellNo.ToString();
		}

		/// <summary>
		/// 指定したセルがボスセルかどうかを判定します。
		/// MapCellNames.json のセル名に "(BOSS)" が含まれていれば true を返します。
		/// </summary>
		/// <param name="mapAreaId">海域ID（例: 7）</param>
		/// <param name="mapInfoNo">マップ番号（例: 4）</param>
		/// <param name="cellNo">セル番号</param>
		/// <returns>ボスセルであれば true</returns>
		public static bool IsBossCell(int mapAreaId, int mapInfoNo, int cellNo)
		{
			var cellName = GetCellName(mapAreaId, mapInfoNo, cellNo);
			return cellName != null
				&& cellName.IndexOf("[ボス]", StringComparison.OrdinalIgnoreCase) >= 0;
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

				// ファイルが更新されていれば再読み込み（タイムスタンプが異なる場合）
				if (lastWriteTime > _lastLoadTime)
				{
					_cellNamesByMap = LoadCellNames();
				}
			}
			catch
			{
			}
		}

		/// <summary>
		/// JSON ファイルからセル名を読み込みます。
		/// </summary>
		private static Dictionary<string, Dictionary<int, string>> LoadCellNames()
		{
			var result = new Dictionary<string, Dictionary<int, string>>();
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
							var cellNames = new Dictionary<int, string>();

							foreach (var cellProp in cellNamesObj.Properties())
							{
								if (int.TryParse(cellProp.Name, out var cellNo))
								{
									cellNames[cellNo] = cellProp.Value?.Value<string>() ?? "";
								}
							}

							result[mapKey] = cellNames;
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
