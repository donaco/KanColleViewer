using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// マップセルの1件分の情報を保持します。
	/// </summary>
	public class CellInfo
	{
		public string Name { get; }
		public bool IsBoss { get; }
		public bool IsKiko { get; }

		public CellInfo(string name, bool isBoss, bool isKiko)
		{
			this.Name = name;
			this.IsBoss = isBoss;
			this.IsKiko = isKiko;
		}
	}

	/// <summary>
	/// マップセルの名称を提供するプロバイダーです。
	/// 海域ごとに異なるセル名マッピングに対応しています。
	/// </summary>
	public static class MapCellNameProvider
	{
		private const int MaxResponseSizeBytes = 1 * 1024 * 1024; // 1 MB
		private const int RequestTimeoutMs = 15000; // 15 seconds
		private const string RemoteMapCellNamesUrl = "https://dona-co.art/kcv/MapCellNames.json";

		/// <summary>
		/// マップID（海域-マップ番号）をキーとした、セル番号→CellInfo のマッピング
		/// </summary>
		private static Dictionary<string, Dictionary<int, CellInfo>> _cellInfoByMap = new Dictionary<string, Dictionary<int, CellInfo>>();

		/// <summary>
		/// 海域ID → 表示用ラベル（例: 62 → "E1"）
		/// </summary>
		private static Dictionary<int, string> _areaLabels = new Dictionary<int, string>();

		private static string _jsonFilePath;
		private static DateTime _lastLoadTime = DateTime.MinValue;
		private static object _lockObj = new object();

		static MapCellNameProvider()
		{
			// exe と同じディレクトリ配下の json\MapCellNames.json を指定
			var executablePath = Assembly.GetEntryAssembly()?.Location
				?? Assembly.GetExecutingAssembly().Location;
			var executableDir = Path.GetDirectoryName(executablePath)
				?? AppDomain.CurrentDomain.BaseDirectory;
			_jsonFilePath = Path.Combine(executableDir, "json", "MapCellNames.json");

			// ① まずローカルで即時初期化（ブロックなし）
			var localContent = LoadFromLocalFile();
			if (!string.IsNullOrEmpty(localContent))
				ParseCellNames(localContent);

			// ② バックグラウンドでリモート取得して差し替え
			ThreadPool.QueueUserWorkItem(_ =>
			{
				try
				{
					var remoteContent = TryLoadFromRemote();
					if (!string.IsNullOrEmpty(remoteContent))
						ParseCellNames(remoteContent);
				}
				catch (Exception ex)
				{
					Debug.WriteLine("MapCellNameProvider: バックグラウンドリモート取得エラー: " + ex.Message);
				}
			});
		}

		/// <summary>
		/// 海域-マップ-セル の表示用テキストを生成します。
		/// ラベルが定義されている場合: (62, 1, "C") → "E1-C"
		/// ラベルが未定義の場合:       (7, 4, "C")  → "7-4-C"
		/// </summary>
		public static string FormatDisplayKey(int mapAreaId, int mapInfoNo, string cellName = null)
		{
			lock (_lockObj)
			{
				TryReloadIfModified();

				// areaLabels に定義がある場合は特殊フォーマット（例: "E1-C"）
				if (_areaLabels != null && _areaLabels.TryGetValue(mapAreaId, out var label))
				{
					var prefix = $"{label}{mapInfoNo}";
					return string.IsNullOrEmpty(cellName)
						? prefix
						: $"{prefix}-{cellName}";
				}

				// 通常海域（例: "7-4-C"）
				return string.IsNullOrEmpty(cellName)
					? $"{mapAreaId}-{mapInfoNo}"
					: $"{mapAreaId}-{mapInfoNo}-{cellName}";
			}
		}

		/// <summary>
		/// 海域IDを表示用ラベルに変換します。
		/// areaLabels に定義があればそのラベル＋mapInfoNo、なければ "mapAreaId-mapInfoNo" を返します。
		/// </summary>
		public static string GetAreaLabel(int mapAreaId, int mapInfoNo)
		{
			lock (_lockObj)
			{
				TryReloadIfModified();
				if (_areaLabels != null && _areaLabels.TryGetValue(mapAreaId, out var label))
				{
					return $"{label}{mapInfoNo}";
				}
				return $"{mapAreaId}-{mapInfoNo}";
			}
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
			lock (_lockObj)
			{
				TryReloadIfModified();

				if (_cellInfoByMap == null)
				{
					Debug.WriteLine("MapCellNameProvider.GetCellInfo: _cellInfoByMap が null です");
					return null;
				}

				var mapKey = $"{mapAreaId}-{mapInfoNo}";

				if (_cellInfoByMap.TryGetValue(mapKey, out var cellInfos))
				{
					if (cellInfos != null && cellInfos.TryGetValue(cellNo, out var info))
					{
						return info;
					}
				}

				return null;
			}
		}

		/// <summary>
		/// 指定したセルがボスセルかどうかを判定します。
		/// MapCellNames.json の boss または kiko フラグに基づいて判定します。
		/// </summary>
		/// <param name="mapAreaId">海域ID（例: 7）</param>
		/// <param name="mapInfoNo">マップ番号（例: 4）</param>
		/// <param name="cellNo">セル番号</param>
		/// <returns>ボスセルであれば true</returns>
		public static bool IsBossCell(int mapAreaId, int mapInfoNo, int cellNo)
		{
			var info = GetCellInfo(mapAreaId, mapInfoNo, cellNo);
			return info != null && (info.IsBoss || info.IsKiko);
		}

		/// <summary>
		/// 指定したセルが帰港セルかどうかを判定します。
		/// MapCellNames.json の kiko フラグに基づいて判定します。
		/// </summary>
		public static bool IsKikoCell(int mapAreaId, int mapInfoNo, int cellNo)
		{
			var info = GetCellInfo(mapAreaId, mapInfoNo, cellNo);
			return info != null && info.IsKiko;
		}

		/// <summary>
		/// JSON ファイルが変更されていれば再読み込みします。
		/// ※呼び出し側で lock(_lockObj) を使用していることを前提とします。
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
					var content = LoadFromLocalFile();
					if (!string.IsNullOrEmpty(content))
					{
						ParseCellNames(content);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("MapCellNameProvider.TryReloadIfModified: エラー: " + ex);
			}
		}

		/// <summary>
		/// リモートサーバー (https://dona-co.art/kcv/MapCellNames.json) から JSON を取得します。
		/// 失敗した場合は null または空の文字列を返します。
		/// </summary>
		private static string TryLoadFromRemote()
		{
			try
			{
				if (!Uri.TryCreate(RemoteMapCellNamesUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
				{
					Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: URI が無効です。 url=" + RemoteMapCellNamesUrl);
					return null;
				}

				var request = WebRequest.Create(uri) as HttpWebRequest;
				if (request == null)
				{
					Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: HttpWebRequest 作成失敗");
					return null;
				}

				request.Timeout = RequestTimeoutMs;
				request.Method = "GET";
				request.UserAgent = "Mozilla/5.0";
				request.Proxy = WebRequest.DefaultWebProxy;

				try
				{
					using (var response = request.GetResponse() as HttpWebResponse)
					{
						if (response == null)
						{
							Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: レスポンスが null です");
							return null;
						}

						if (response.StatusCode == HttpStatusCode.OK)
						{
							using (var stream = response.GetResponseStream())
							{
								if (stream == null)
								{
									Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: ストリームが null です");
									return null;
								}

								using (var reader = new StreamReader(stream))
								{
									var content = reader.ReadToEnd();
									if (content.Length <= MaxResponseSizeBytes)
									{
										Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: リモート読込成功 (サイズ: " + content.Length + " bytes)");
										return content;
									}
									Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: レスポンスサイズが大きすぎます");
									return null;
								}
							}
						}
						else
						{
							Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: HTTP失敗 " + (int)response.StatusCode + " " + response.StatusDescription);
							return null;
						}
					}
				}
				catch (WebException ex)
				{
					Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: WebException: " + ex.Status + " - " + ex.Message);
					return null;
				}
				catch (TimeoutException ex)
				{
					Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: Timeout: " + ex);
					return null;
				}
				catch (Exception ex)
				{
					Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: Unexpected: " + ex);
					return null;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("MapCellNameProvider.TryLoadFromRemote: 予期しないエラー: " + ex);
				return null;
			}
		}

		/// <summary>
		/// ローカルファイルから MapCellNames.json を読み込みます。
		/// </summary>
		private static string LoadFromLocalFile()
		{
			try
			{
				if (!File.Exists(_jsonFilePath))
				{
					Debug.WriteLine("MapCellNameProvider.LoadFromLocalFile: ファイルなし: " + _jsonFilePath);
					return null;
				}

				var content = File.ReadAllText(_jsonFilePath);
				Debug.WriteLine("MapCellNameProvider.LoadFromLocalFile: ローカル読込成功");
				return content;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("MapCellNameProvider.LoadFromLocalFile: 失敗: " + ex);
				return null;
			}
		}

		/// <summary>
		/// JSON 文字列を解析し、セル名と海域ラベルを読み込みます。
		/// 文字列値 → CellInfo(name, boss: false, kiko: false)
		/// オブジェクト値 → CellInfo(name, boss, kiko) として読み込みます。
		/// </summary>
		private static void ParseCellNames(string content)
		{
			lock (_lockObj)
			{
				var result = new Dictionary<string, Dictionary<int, CellInfo>>();
				var labels = new Dictionary<int, string>();

				try
				{
					if (string.IsNullOrEmpty(content))
					{
						Debug.WriteLine("MapCellNameProvider.ParseCellNames: コンテンツが空です");
						return;
					}

					JObject root;
					try
					{
						root = JObject.Parse(content);
					}
					catch (Exception parseEx)
					{
						Debug.WriteLine("MapCellNameProvider.ParseCellNames: JSON解析エラー: " + parseEx.Message);
						return;
					}

					if (root == null)
					{
						Debug.WriteLine("MapCellNameProvider.ParseCellNames: パースされたrootが null です");
						return;
					}

					// areaLabels の読み込み（例: "62" → "E"）
					var labelsObj = root["areaLabels"] as JObject;
					if (labelsObj != null)
					{
						foreach (var p in labelsObj.Properties())
						{
							try
							{
								if (int.TryParse(p.Name, out var id))
								{
									if (p.Value != null)
									{
										var value = p.Value.Value<string>();
										if (value != null)
										{
											labels[id] = value;
										}
									}
								}
							}
							catch (Exception labelEx)
							{
								Debug.WriteLine("MapCellNameProvider.ParseCellNames: areaLabel解析エラー (key=" + p.Name + "): " + labelEx.Message);
							}
						}
					}

					// maps の読み込み
					var maps = root["maps"] as JObject;
					if (maps != null)
					{
						foreach (var mapProp in maps.Properties())
						{
							try
							{
								var mapKey = mapProp.Name;
								var cellNamesObj = mapProp.Value as JObject;

								if (cellNamesObj != null)
								{
									var cellInfos = new Dictionary<int, CellInfo>();

									foreach (var cellProp in cellNamesObj.Properties())
									{
										try
										{
											if (int.TryParse(cellProp.Name, out var cellNo))
											{
												if (cellProp.Value == null)
												{
													Debug.WriteLine("MapCellNameProvider.ParseCellNames: cellValue が null (map=" + mapKey + ", cellNo=" + cellNo + ")");
													continue;
												}

												CellInfo info;

												if (cellProp.Value.Type == JTokenType.Object)
												{
													// オブジェクト形式: { "name": "C", "boss": true, "kiko": true }
													var obj = cellProp.Value as JObject;
													var name = obj?["name"]?.Value<string>() ?? "";
													var boss = obj?["boss"]?.Value<bool>() ?? false;
													var kiko = obj?["kiko"]?.Value<bool>() ?? false;
													info = new CellInfo(name, boss, kiko);
												}
												else if (cellProp.Value.Type == JTokenType.String)
												{
													// 文字列形式（従来互換）: "A"
													var name = cellProp.Value.Value<string>();
													if (name == null)
													{
														Debug.WriteLine("MapCellNameProvider.ParseCellNames: 文字列値が null (map=" + mapKey + ", cellNo=" + cellNo + ")");
														continue;
													}
													info = new CellInfo(name, false, false);
												}
												else
												{
													// その他の型
													Debug.WriteLine("MapCellNameProvider.ParseCellNames: 予期しない型 (map=" + mapKey + ", cellNo=" + cellNo + ", type=" + cellProp.Value.Type + ")");
													continue;
												}

												cellInfos[cellNo] = info;
											}
										}
										catch (Exception cellEx)
										{
											Debug.WriteLine("MapCellNameProvider.ParseCellNames: セルデータ解析エラー (map=" + mapKey + ", cell=" + cellProp.Name + "): " + cellEx.Message);
										}
									}

									result[mapKey] = cellInfos;
								}
							}
							catch (Exception mapEx)
							{
								Debug.WriteLine("MapCellNameProvider.ParseCellNames: マップ解析エラー (mapKey=" + mapProp.Name + "): " + mapEx.Message);
							}
						}
					}

					// 成功時に静的フィールドを更新
					_cellInfoByMap = result;
					_areaLabels = labels;
					_lastLoadTime = DateTime.Now;

					Debug.WriteLine("MapCellNameProvider.ParseCellNames: 読込完了 (areaLabels=" + _areaLabels.Count + ", maps=" + result.Count + ")");
				}
				catch (Exception ex)
				{
					Debug.WriteLine("MapCellNameProvider.ParseCellNames: 予期しないエラー: " + ex);
				}
			}
		}
	}
}
