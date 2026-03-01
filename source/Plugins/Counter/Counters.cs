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

		public void Reset()
		{
			this.Count = 0;
		}
	}

	public class ItemDestroyCounter : CounterBase
	{
		public ItemDestroyCounter(KanColleProxy proxy)
		{
			KanColleClient.Current.ItemDestroyed += (sender, e) =>
			{
				System.Diagnostics.Debug.WriteLine("[Counter] ItemDestroyed イベント発火!");
				this.Count++;
			};

			this.Text = "装備を破棄した回数";
		}
	}

	public class SupplyCounter : CounterBase
	{
		public SupplyCounter(KanColleProxy proxy)
		{
			KanColleClient.Current.SupplyCompleted += (sender, e) =>
			{
				System.Diagnostics.Debug.WriteLine("[Counter] SupplyCompleted イベント発火!");
				this.Count++;
			};

			this.Text = "艦娘に補給した回数";
		}
	}

	public class MissionCounter : CounterBase
	{
		public MissionCounter(KanColleProxy proxy)
		{
			KanColleClient.Current.MissionSucceeded += (sender, e) =>
			{
				System.Diagnostics.Debug.WriteLine("[Counter] MissionSucceeded イベント発火!");
				this.Count++;
			};

			this.Text = "遠征に成功した回数";
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
		/// セル名（例: "O"）。セルに到達しなかった場合は null
		/// </summary>
		public string CellName { get; }

		/// <summary>
		/// 戦闘結果ランク（例: "S"）。
		/// </summary>
		public string WinRank { get; }

		/// <summary>
		/// 記録日時
		/// </summary>
		public DateTime Timestamp { get; }

		/// <summary>
		/// 海域-セルのキー文字列（例: "7-4-C"）。集計に使用します。
		/// </summary>
		public string AreaCellKey { get; }

		public SortieRecord(int mapAreaId, int mapInfoNo, int? cellNo, string winRank)
		{
			this.MapAreaId = mapAreaId;
			this.MapInfoNo = mapInfoNo;
			this.WinRank = winRank;
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
	/// 海域-セルごとの出撃数を表すモデルです。
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

		public SortieAreaCount(string areaCellKey)
		{
			this.AreaCellKey = areaCellKey;
			this.Count = 0;
		}

		/// <summary>
		/// カウントを 1 増加します。
		/// </summary>
		public void Increment()
		{
			this.Count++;
		}
	}

	/// <summary>
	/// 直近の出撃履歴を保持・表示するカウンターです。
	/// 戦闘結果（WinRank）を受け取るたびに即座に履歴を追加します。
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
			this._sortieInfo = KanColleClient.Current.SortieInfo;
			this._sortieInfo.PropertyChanged += this.SortieInfo_PropertyChanged;
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

						this.AddRecord(
							this._currentMapAreaId,
							this._currentMapInfoNo,
							cellNo,
							sortieInfo.WinRank
						);
					}
					break;

				case nameof(SortieInfo.IsActive):
					if (sortieInfo.IsActive)
					{
						// 出撃開始: 海域情報を取得し、セル情報をリセット
						this._currentMapAreaId = sortieInfo.MapAreaId;
						this._currentMapInfoNo = sortieInfo.MapInfoNo;
						this._currentCellNo = null;
					}
					break;
			}
		}

		/// <summary>
		/// 履歴を1件追加し、海域ごとの出撃数を更新します。
		/// UI スレッドで安全に実行します。
		/// </summary>
		private void AddRecord(int mapAreaId, int mapInfoNo, int? cellNo, string winRank)
		{
			var record = new SortieRecord(mapAreaId, mapInfoNo, cellNo, winRank);

			System.Diagnostics.Debug.WriteLine($"[SortieHistory] 履歴追加: {record.DisplayText}");

			var app = System.Windows.Application.Current;
			if (app != null)
			{
				app.Dispatcher.BeginInvoke((Action)(() =>
				{
					// 履歴に追加
					this.History.Insert(0, record);
					while (this.History.Count > this._maxHistory)
					{
						this.History.RemoveAt(this.History.Count - 1);
					}

					// 海域ごとの出撃数を更新
					this.UpdateAreaCount(record.AreaCellKey);
				}));
			}
			else
			{
				this.History.Insert(0, record);
				while (this.History.Count > this._maxHistory)
				{
					this.History.RemoveAt(this.History.Count - 1);
				}
				this.UpdateAreaCount(record.AreaCellKey);
			}
		}

		/// <summary>
		/// 海域-セルごとの出擊数を更新します。
		/// 既存のキーがあればカウントを増加し、なければ新しいエントリを追加します。
		/// 追加後は出撃数の多い順にソートします。
		/// </summary>
		private void UpdateAreaCount(string areaCellKey)
		{
			if (this._areaCountMap.TryGetValue(areaCellKey, out var existing))
			{
				// 既存エントリのカウントを増加
				existing.Increment();
			}
			else
			{
				// 新しいエントリを作成
				var newEntry = new SortieAreaCount(areaCellKey);
				newEntry.Increment();
				this._areaCountMap[areaCellKey] = newEntry;
				this.AreaCounts.Add(newEntry);
			}

			// 出撃数の多い順にソート（ObservableCollection を入れ替え）
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
