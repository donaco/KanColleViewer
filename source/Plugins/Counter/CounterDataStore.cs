using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grabacr07.KanColleWrapper.Models;
using Newtonsoft.Json;

namespace Counter
{
	/// <summary>
	/// カウンターデータの保存・読み込みを担当するクラスです。
	/// %LocalAppData%\grabacr.net\KanColleViewer\CounterData.json に JSON 形式で保存します。
	/// </summary>
	public static class CounterDataStore
	{
		private static readonly string FilePath;

		static CounterDataStore()
		{
			var dir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"grabacr.net",
				"KanColleViewer");

			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			FilePath = Path.Combine(dir, "CounterData.json");
		}

		/// <summary>
		/// 保存用データモデル
		/// </summary>
		public class CounterSaveData
		{
			public Dictionary<string, int> Counters { get; set; } = new Dictionary<string, int>();
			public List<AreaCountData> AreaCounts { get; set; } = new List<AreaCountData>();
			public List<HistoryData> History { get; set; } = new List<HistoryData>();
			public DateTime SavedAt { get; set; }

			// 追加: 表示設定（旧JSON互換のため nullable）
			public bool? IsCounterEnabled { get; set; }
			public bool? IsSortieHistoryEnabled { get; set; }
			public bool? ShowAirSuperiority { get; set; }
			public bool? BossOnly { get; set; }
			public bool? IsTopMost { get; set; }
		}

		/// <summary>
		/// 海域ごとの集計データの保存モデル
		/// </summary>
		public class AreaCountData
		{
			public string AreaCellKey { get; set; }
			public int? MapAreaId { get; set; }
			public int? MapInfoNo { get; set; }
			public int? CellNo { get; set; }
			public string CellName { get; set; }
			public int Count { get; set; }
			public int SCount { get; set; }
			public int ACount { get; set; }
			public int BCount { get; set; }
			public int AirSupremacyCount { get; set; }
			public int AirSuperiorCount { get; set; }
			public int DestructionCount { get; set; }
			public int LdAirbattleCount { get; set; }
		}

		/// <summary>
		/// 戦闘履歴の保存モデル
		/// </summary>
		public class HistoryData
		{
			public int MapAreaId { get; set; }
			public int MapInfoNo { get; set; }
			public int? CellNo { get; set; }
			public string WinRank { get; set; }
			public int AirResult { get; set; }
			public bool IsDestruction { get; set; }
			public bool IsLdAirbattle { get; set; }
			public string Timestamp { get; set; }
		}

		/// <summary>
		/// カウンターデータを JSON ファイルに保存します。
		/// </summary>
		public static void Save(
			IEnumerable<CounterBase> counters,
			SortieHistoryCounter sortieHistory,
			bool isCounterEnabled,
			bool isSortieHistoryEnabled,
			bool showAirSuperiority,
			bool isTopMost)
		{
			try
			{
				var data = new CounterSaveData
				{
					SavedAt = DateTime.Now,

					// 追加: 表示設定保存
					IsCounterEnabled = isCounterEnabled,
					IsSortieHistoryEnabled = isSortieHistoryEnabled,
					ShowAirSuperiority = showAirSuperiority,
					BossOnly = sortieHistory?.BossOnly,
					IsTopMost = isTopMost,
				};

				// 各カウンターの値を保存
				if (counters != null)
				{
					foreach (var c in counters)
					{
						if (!string.IsNullOrEmpty(c.Text))
						{
							data.Counters[c.Text] = c.Count;
						}
					}
				}

				if (sortieHistory != null)
				{
					// 海域ごとの出撃数を保存
					if (sortieHistory.AreaCounts != null)
					{
						foreach (var ac in sortieHistory.AreaCounts)
						{
							data.AreaCounts.Add(new AreaCountData
							{
								AreaCellKey = ac.AreaCellKey,
								MapAreaId = ac.MapAreaId,
								MapInfoNo = ac.MapInfoNo,
								CellNo = ac.CellNo,
								CellName = ac.CellName,
								Count = ac.Count,
								SCount = ac.SCount,
								ACount = ac.ACount,
								BCount = ac.BCount,
								AirSupremacyCount = ac.AirSupremacyCount,
								AirSuperiorCount = ac.AirSuperiorCount,
								DestructionCount = ac.DestructionCount,
								LdAirbattleCount = ac.LdAirbattleCount,
							});
						}
					}

					// 戦闘履歴を保存
					if (sortieHistory.History != null)
					{
						foreach (var record in sortieHistory.History)
						{
							data.History.Add(new HistoryData
							{
								MapAreaId = record.MapAreaId,
								MapInfoNo = record.MapInfoNo,
								CellNo = record.CellNo,
								WinRank = record.WinRank,
								AirResult = (int)record.AirResult,
								IsDestruction = record.IsDestruction,
								IsLdAirbattle = record.IsLdAirbattle,
								Timestamp = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
							});
						}
					}
				}

				var json = JsonConvert.SerializeObject(data, Formatting.Indented);
				File.WriteAllText(FilePath, json);

				System.Diagnostics.Debug.WriteLine($"[Counter] データ保存完了: {FilePath}");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Counter] データ保存失敗: {ex.Message}");
			}
		}

		/// <summary>
		/// JSON ファイルからカウンターデータを読み込みます。
		/// </summary>
		public static CounterSaveData Load()
		{
			try
			{
				if (!File.Exists(FilePath))
				{
					System.Diagnostics.Debug.WriteLine("[Counter] 保存データなし。新規起動します。");
					return null;
				}

				var json = File.ReadAllText(FilePath);
				var data = JsonConvert.DeserializeObject<CounterSaveData>(json);

				System.Diagnostics.Debug.WriteLine($"[Counter] データ読み込み完了: 保存日時={data?.SavedAt}");
				return data;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Counter] データ読み込み失敗: {ex.Message}");
				return null;
			}
		}
	}
}
