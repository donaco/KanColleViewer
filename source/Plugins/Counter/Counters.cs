using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using Livet;

namespace Counter
{
	public abstract class CounterBase : NotificationObject
	{
		#region Text 変更通知プロパティ

		private string _Text;

		public string Text
		{
			get { return this._Text; }
			set
			{
				if (this._Text != value)
				{
					this._Text = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Count 変更通知プロパティ

		private int _Count;

		public int Count
		{
			get { return this._Count; }
			set
			{
				if (this._Count != value)
				{
					this._Count = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsEnabled 変更通知プロパティ

		private bool _IsEnabled = true;

		/// <summary>
		/// カウントの有効/無効を切り替えます。false の場合、カウントアップをスキップします。
		/// </summary>
		public bool IsEnabled
		{
			get { return this._IsEnabled; }
			set
			{
				if (this._IsEnabled != value)
				{
					this._IsEnabled = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public void Reset()
		{
			this.Count = 0;
		}
	}

	/// <summary>
	/// 補給をカウント
	/// </summary>
	public class SupplyCounter : CounterBase
	{
		public SupplyCounter(KanColleProxy proxy)
		{
			KanColleClient.Current.SupplyCompleted += (sender, e) =>
			{
				if (!this.IsEnabled) return;
				System.Diagnostics.Debug.WriteLine("[Counter] SupplyCompleted イベント発火!");
				this.Count++;
			};

			this.Text = "艦娘に補給した回数";
		}
	}

	/// <summary>
	/// 装備の破棄をカウント
	/// </summary>
	public class ItemDestroyCounter : CounterBase
	{
		public ItemDestroyCounter(KanColleProxy proxy)
		{
			KanColleClient.Current.ItemDestroyed += (sender, e) =>
			{
				if (!this.IsEnabled) return;
				System.Diagnostics.Debug.WriteLine("[Counter] ItemDestroyed イベント発火!");
				this.Count++;
			};

			this.Text = "装備を破棄した回数";
		}
	}

	/// <summary>
	/// 遠征の成功をカウント
	/// </summary>
	public class MissionCounter : CounterBase
	{
		public MissionCounter(KanColleProxy proxy)
		{
			KanColleClient.Current.MissionSucceeded += (sender, e) =>
			{
				if (!this.IsEnabled) return;
				System.Diagnostics.Debug.WriteLine("[Counter] MissionSucceeded イベント発火!");
				this.Count++;
			};

			this.Text = "遠征に成功した回数";
		}
	}

	/// <summary>
	/// 出撃をカウント
	/// </summary>
	public class SortieCounter : CounterBase
	{
		public SortieCounter(KanColleProxy proxy)
		{
			// SortieInfo.IsActive が true になったタイミング（= api_req_map/start）でカウント
			KanColleClient.Current.SortieInfo.PropertyChanged += (sender, e) =>
			{
				if (!this.IsEnabled) return;
				if (e.PropertyName == nameof(SortieInfo.IsActive)
					&& KanColleClient.Current.SortieInfo.IsActive)
				{
					System.Diagnostics.Debug.WriteLine("[Counter] 出撃開始検知!");
					this.Count++;
				}
			};

			this.Text = "海域に出撃した回数";
		}
	}

	/// <summary>
	/// 出撃履歴の1件分を表すモデルです。
	/// </summary>
	public class SortieRecord : NotificationObject
	{
		/// <summary>
		/// 表示テキスト（例: "7-4-O [S]"）
		/// </summary>
		public string DisplayText { get; }

		/// <summary>
		/// 海域 ID（例: 7）
		/// </summary>
		public int MapAreaId { get; }

		/// <summary>
		/// マップ番号（例: 4）
		/// </summary>
		public int MapInfoNo { get; }

		/// <summary>
		/// セル名（例: "O" または "O [ボス]"）。セルに到達しなかった場合は null
		/// </summary>
		public string CellName { get; }

		/// <summary>
		/// 戦闘結果ランク（例: "S"）。
		/// </summary>
		public string WinRank { get; }

		/// <summary>
		/// 航空戦の制空状態（例: AirSuperiority.AirSupremacy）
		/// </summary>
		public AirSuperiority AirResult { get; }

		/// <summary>
		/// 記録日時
		/// </summary>
		public DateTime Timestamp { get; }

		/// <summary>
		/// 海域-セルのキー文字列（例: "7-4-C"）。集計に使用します。
		/// </summary>
		public string AreaCellKey { get; }

		/// <summary>
		/// "(BOSS)" 部分を取り除いた海域表示（例: "7-4-K"）。
		/// </summary>
		public string CleanAreaCellKey
		{
			get
			{
				// CellName が null の場合は "mapArea-mapInfo" を返す
				if (string.IsNullOrEmpty(this.CellName)) return $"{this.MapAreaId}-{this.MapInfoNo}";

				// "(BOSS)" を取り除く
				var cleanCell = this.CellName.Replace("[ボス]", "").Trim();

				return !string.IsNullOrEmpty(cleanCell)
					? $"{this.MapAreaId}-{this.MapInfoNo}-{cleanCell}"
					: $"{this.MapAreaId}-{this.MapInfoNo}";
			}
		}

		/// <summary>
		/// ボス表示テキスト（ボスセルなら "[ボス]"、そうでなければ空文字）。
		/// </summary>
		public string BossText
		{
			get
			{
				return !string.IsNullOrEmpty(this.CellName) &&
					   this.CellName.IndexOf("[ボス]", StringComparison.OrdinalIgnoreCase) >= 0
					? "[ボス]"
					: string.Empty;
			}
		}

		/// <summary>
		/// 制空状態の表示テキスト（例: "[確保]"）。AirResult が None なら空文字。
		/// </summary>
		public string AirSuperiorityText
		{
			get
			{
				switch (this.AirResult)
				{
					case AirSuperiority.AirParity: return "[均衡]";
					case AirSuperiority.AirSupremacy: return "[確保]";
					case AirSuperiority.AirSuperior: return "[優勢]";
					case AirSuperiority.AirInferior: return "[劣勢]";
					case AirSuperiority.AirIncapability: return "[喪失]";
					default: return string.Empty;
				}
			}
		}

		public SortieRecord(int mapAreaId, int mapInfoNo, int? cellNo, string winRank, AirSuperiority airResult = AirSuperiority.None)
		{
			this.MapAreaId = mapAreaId;
			this.MapInfoNo = mapInfoNo;
			this.WinRank = winRank;
			this.AirResult = airResult;
			this.Timestamp = DateTime.Now;

			if (cellNo.HasValue && cellNo.Value > 0)
			{
				this.CellName = MapCellNameProvider.GetCellName(mapAreaId, mapInfoNo, cellNo.Value);
			}

			// 海域-セルのキー（例: "7-4-C"、セル無しなら "7-4"）
			this.AreaCellKey = !string.IsNullOrEmpty(this.CellName)
				? $"{mapAreaId}-{mapInfoNo}-{this.CellName}"
				: $"{mapAreaId}-{mapInfoNo}";

			// 表示テキスト（例: "7-4-C [S]"）
			var text = this.AreaCellKey;
			if (!string.IsNullOrEmpty(winRank))
			{
				text += $" [{winRank}]";
			}
			this.DisplayText = text;
		}
	}

	/// <summary>
	/// 海域-セルごとの出撃数と戦闘結果の集計を表すモデルです。
	/// </summary>
	public class SortieAreaCount : NotificationObject
	{
		/// <summary>
		/// 海域-セルのキー（例: "7-4-C"）
		/// </summary>
		public string AreaCellKey { get; }

		#region Count 変更通知プロパティ

		private int _Count;

		/// <summary>
		/// 出撃回数
		/// </summary>
		public int Count
		{
			get { return this._Count; }
			set
			{
				if (this._Count != value)
				{
					this._Count = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SCount 変更通知プロパティ

		private int _SCount;

		/// <summary>
		/// S勝利の回数
		/// </summary>
		public int SCount
		{
			get { return this._SCount; }
			set
			{
				if (this._SCount != value)
				{
					this._SCount = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region ACount 変更通知プロパティ

		private int _ACount;

		/// <summary>
		/// A勝利の回数
		/// </summary>
		public int ACount
		{
			get { return this._ACount; }
			set
			{
				if (this._ACount != value)
				{
					this._ACount = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region BCount 変更通知プロパティ

		private int _BCount;

		/// <summary>
		/// B勝利の回数
		/// </summary>
		public int BCount
		{
			get { return this._BCount; }
			set
			{
				if (this._BCount != value)
				{
					this._BCount = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AirSupremacyCount 変更通知プロパティ

		private int _AirSupremacyCount;

		/// <summary>
		/// 制空確保の回数
		/// </summary>
		public int AirSupremacyCount
		{
			get { return this._AirSupremacyCount; }
			set
			{
				if (this._AirSupremacyCount != value)
				{
					this._AirSupremacyCount = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AirSuperiorCount 変更通知プロパティ

		private int _AirSuperiorCount;

		/// <summary>
		/// 航空優勢の回数
		/// </summary>
		public int AirSuperiorCount
		{
			get { return this._AirSuperiorCount; }
			set
			{
				if (this._AirSuperiorCount != value)
				{
					this._AirSuperiorCount = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		/// <summary>
		/// S勝利の表示テキスト（0の場合は空文字）
		/// </summary>
		public string SText => this.SCount > 0 ? $"S:{this.SCount}" : "";

		/// <summary>
		/// A勝利の表示テキスト（0の場合は空文字）
		/// </summary>
		public string AText => this.ACount > 0 ? $"A:{this.ACount}" : "";

		/// <summary>
		/// B勝利の表示テキスト（0の場合は空文字）
		/// </summary>
		public string BText => this.BCount > 0 ? $"B:{this.BCount}" : "";

		/// <summary>
		/// 制空確保の表示テキスト（0の場合は空文字）
		/// </summary>
		public string AirSupremacyText => this.AirSupremacyCount > 0 ? $"確:{this.AirSupremacyCount}" : "";

		/// <summary>
		/// 航空優勢の表示テキスト（0の場合は空文字）
		/// </summary>
		public string AirSuperiorText => this.AirSuperiorCount > 0 ? $"優:{this.AirSuperiorCount}" : "";

		public SortieAreaCount(string areaCellKey)
		{
			this.AreaCellKey = areaCellKey;
			this.Count = 0;
		}

		/// <summary>
		/// カウントを 1 増加し、ランク別・制空別の集計も更新します。
		/// </summary>
		/// <param name="winRank">戦闘結果ランク（"S", "A", "B" など）</param>
		/// <param name="airResult">航空戦の制空状態</param>
		public void Increment(string winRank, AirSuperiority airResult = AirSuperiority.None)
		{
			this.Count++;

			switch (winRank)
			{
				case "S":
					this.SCount++;
					this.RaisePropertyChanged(nameof(this.SText));
					break;
				case "A":
					this.ACount++;
					this.RaisePropertyChanged(nameof(this.AText));
					break;
				case "B":
					this.BCount++;
					this.RaisePropertyChanged(nameof(this.BText));
					break;
			}

			switch (airResult)
			{
				case AirSuperiority.AirSupremacy:
					this.AirSupremacyCount++;
					this.RaisePropertyChanged(nameof(this.AirSupremacyText));
					break;
				case AirSuperiority.AirSuperior:
					this.AirSuperiorCount++;
					this.RaisePropertyChanged(nameof(this.AirSuperiorText));
					break;
			}
		}
	}

	/// <summary>
	/// 直近の出撃履歴を保持・表示するカウンターです。
	/// BossOnly が true の場合、ボスセルでの戦闘結果のみを記録します。
	/// </summary>
	public class SortieHistoryCounter : NotificationObject
	{
		private readonly int _maxHistory;
		private readonly SortieInfo _sortieInfo;

		// 出撃中の海域・セル情報を一時保持
		private int _currentMapAreaId;
		private int _currentMapInfoNo;
		private int? _currentCellNo;

		// 海域-セルごとの集計データ（キー検索用）
		private readonly Dictionary<string, SortieAreaCount> _areaCountMap;

		#region IsEnabled 変更通知プロパティ

		private bool _IsEnabled = true;

		/// <summary>
		/// 戦闘履歴・出撃数カウントの有効/無効を切り替えます。false の場合、記録をスキップします。
		/// </summary>
		public bool IsEnabled
		{
			get { return this._IsEnabled; }
			set
			{
				if (this._IsEnabled != value)
				{
					this._IsEnabled = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region BossOnly 変更通知プロパティ

		private bool _BossOnly;

		/// <summary>
		/// true の場合、ボスセルでの戦闘結果のみをカウント・履歴に追加します。
		/// </summary>
		public bool BossOnly
		{
			get { return this._BossOnly; }
			set
			{
				if (this._BossOnly != value)
				{
					this._BossOnly = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region History 変更通知プロパティ

		private ObservableCollection<SortieRecord> _History;

		/// <summary>
		/// 直近の出撃履歴（新しいものが先頭）
		/// </summary>
		public ObservableCollection<SortieRecord> History
		{
			get { return this._History; }
			set
			{
				if (this._History != value)
				{
					this._History = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AreaCounts 変更通知プロパティ

		private ObservableCollection<SortieAreaCount> _AreaCounts;

		/// <summary>
		/// 海域-セルごとの出撃数一覧（出撃数の多い順）
		/// </summary>
		public ObservableCollection<SortieAreaCount> AreaCounts
		{
			get { return this._AreaCounts; }
			set
			{
				if (this._AreaCounts != value)
				{
					this._AreaCounts = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="proxy">KanColleProxy のインスタンス</param>
		/// <param name="maxHistory">保持する履歴の最大件数</param>
		public SortieHistoryCounter(KanColleProxy proxy, int maxHistory = 20)
		{
			this._maxHistory = maxHistory;
			this.History = new ObservableCollection<SortieRecord>();
			this.AreaCounts = new ObservableCollection<SortieAreaCount>();
			this._areaCountMap = new Dictionary<string, SortieAreaCount>();

			// SortieInfo への参照を保持し、PropertyChanged を監視
			var sortieInfo = KanColleClient.Current?.SortieInfo;
			if (sortieInfo != null)
			{
				this._sortieInfo = sortieInfo;
				this._sortieInfo.PropertyChanged += this.SortieInfo_PropertyChanged;
			}
			else
			{
				System.Diagnostics.Debug.WriteLine("[SortieHistory] SortieInfo が null のため、履歴記録は無効です。");
			}
		}

		private void SortieInfo_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			var sortieInfo = (SortieInfo)sender;

			switch (e.PropertyName)
			{
				case nameof(SortieInfo.MapAreaId):
				case nameof(SortieInfo.MapInfoNo):
					// 海域情報を常に最新に保つ
					this._currentMapAreaId = sortieInfo.MapAreaId;
					this._currentMapInfoNo = sortieInfo.MapInfoNo;
					break;

				case nameof(SortieInfo.CellNo):
					if (sortieInfo.CellNo.HasValue)
					{
						this._currentCellNo = sortieInfo.CellNo;
					}
					break;

				case nameof(SortieInfo.WinRank):
					// 無効の場合はスキップ
					if (!this.IsEnabled) break;

					// 戦闘結果が来たタイミングで即座に履歴を追加
					if (!string.IsNullOrEmpty(sortieInfo.WinRank)
						&& this._currentMapAreaId > 0
						&& this._currentMapInfoNo > 0)
					{
						// SortieInfo から直接 CellNo を読み取り、ローカルキャッシュとマージ
						var cellNo = this._currentCellNo;
						var liveCellNo = this._sortieInfo.CellNo;
						if (liveCellNo.HasValue && liveCellNo.Value > 0)
						{
							cellNo = liveCellNo;
						}

						// BossOnly フィルター: MapCellNames.json のセル名に "[ボス]" が含まれるかで判定
						if (this.BossOnly)
						{
							if (!cellNo.HasValue
								|| !MapCellNameProvider.IsBossCell(this._currentMapAreaId, this._currentMapInfoNo, cellNo.Value))
							{
								System.Diagnostics.Debug.WriteLine(
									$"[SortieHistory] BossOnly フィルター: {this._currentMapAreaId}-{this._currentMapInfoNo} cellNo={cellNo} → ボスセルではないためスキップ");
								break;
							}
						}

						// AirResult を取得して AddRecord に渡す
						this.AddRecord(
							this._currentMapAreaId,
							this._currentMapInfoNo,
							cellNo,
							sortieInfo.WinRank,
							sortieInfo.AirResult
						);
					}
					break;

				case nameof(SortieInfo.IsActive):
					if (sortieInfo.IsActive)
					{
						this._currentMapAreaId = sortieInfo.MapAreaId;
						this._currentMapInfoNo = sortieInfo.MapInfoNo;
						this._currentCellNo = null;
					}
					break;
			}
		}

		/// <summary>
		/// 履歴を1件追加し、海域ごとの出撃数を更新します。
		/// </summary>
		private void AddRecord(int mapAreaId, int mapInfoNo, int? cellNo, string winRank, AirSuperiority airResult)
		{
			var record = new SortieRecord(mapAreaId, mapInfoNo, cellNo, winRank, airResult);

			System.Diagnostics.Debug.WriteLine($"[SortieHistory] 履歴追加: {record.DisplayText}");

			var app = System.Windows.Application.Current;
			if (app != null)
			{
				app.Dispatcher.BeginInvoke((Action)(() =>
				{
					this.History.Insert(0, record);
					while (this.History.Count > this._maxHistory)
					{
						this.History.RemoveAt(this.History.Count - 1);
					}

					this.UpdateAreaCount(record.AreaCellKey, winRank, airResult);
				}));
			}
			else
			{
				this.History.Insert(0, record);
				while (this.History.Count > this._maxHistory)
				{
					this.History.RemoveAt(this.History.Count - 1);
				}
				this.UpdateAreaCount(record.AreaCellKey, winRank, airResult);
			}
		}

		/// <summary>
		/// 海域-セルごとの出撃数とランク別・制空別集計を更新します。
		/// </summary>
		private void UpdateAreaCount(string areaCellKey, string winRank, AirSuperiority airResult)
		{
			if (this._areaCountMap.TryGetValue(areaCellKey, out var existing))
			{
				existing.Increment(winRank, airResult);
			}
			else
			{
				var newEntry = new SortieAreaCount(areaCellKey);
				newEntry.Increment(winRank, airResult);
				this._areaCountMap[areaCellKey] = newEntry;
				this.AreaCounts.Add(newEntry);
			}

			// 出撃数の多い順にソート
			var sorted = this.AreaCounts.OrderByDescending(x => x.Count).ToList();
			for (int i = 0; i < sorted.Count; i++)
			{
				var currentIndex = this.AreaCounts.IndexOf(sorted[i]);
				if (currentIndex != i)
				{
					this.AreaCounts.Move(currentIndex, i);
				}
			}
		}

		/// <summary>
		/// 戦闘履歴のみをクリアします。
		/// </summary>
		public void ResetHistory()
		{
			var app = System.Windows.Application.Current;
			if (app != null)
			{
				app.Dispatcher.BeginInvoke((Action)(() =>
				{
					this.History.Clear();
				}));
			}
			else
			{
				this.History.Clear();
			}
		}

		/// <summary>
		/// 海域ごとの出撃数のみをクリアします。
		/// </summary>
		public void ResetAreaCounts()
		{
			var app = System.Windows.Application.Current;
			if (app != null)
			{
				app.Dispatcher.BeginInvoke((Action)(() =>
				{
					this.AreaCounts.Clear();
					this._areaCountMap.Clear();
				}));
			}
			else
			{
				this.AreaCounts.Clear();
				this._areaCountMap.Clear();
			}
		}

		/// <summary>
		/// 履歴と集計データをすべてクリアします。
		/// </summary>
		public void Reset()
		{
			var app = System.Windows.Application.Current;
			if (app != null)
			{
				app.Dispatcher.BeginInvoke((Action)(() =>
				{
					this.History.Clear();
					this.AreaCounts.Clear();
					this._areaCountMap.Clear();
				}));
			}
			else
			{
				this.History.Clear();
				this.AreaCounts.Clear();
				this._areaCountMap.Clear();
			}
		}
	}
}
