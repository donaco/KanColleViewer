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

			var text = $"{mapAreaId}-{mapInfoNo}";
			if (!string.IsNullOrEmpty(this.CellName))
			{
				text += $"-{this.CellName}";
			}
			if (!string.IsNullOrEmpty(winRank))
			{
				text += $" [{winRank}]";
			}
			this.DisplayText = text;
		}
	}

	/// <summary>
	/// 直近の出撃履歴を保持・表示するカウンターです。
	/// 戦闘結果（WinRank）を受け取るたびに即座に履歴を追加します。
	/// </summary>
	public class SortieHistoryCounter : NotificationObject
	{
		private readonly int _maxHistory;

		// 出撃中の海域・セル情報を一時保持
		private int _currentMapAreaId;
		private int _currentMapInfoNo;
		private int? _currentCellNo;

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

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="proxy">KanColleProxy のインスタンス</param>
		/// <param name="maxHistory">保持する履歴の最大件数</param>
		public SortieHistoryCounter(KanColleProxy proxy, int maxHistory = 20)
		{
			this._maxHistory = maxHistory;
			this.History = new ObservableCollection<SortieRecord>();

			// SortieInfo の PropertyChanged を監視
			var sortieInfo = KanColleClient.Current.SortieInfo;
			sortieInfo.PropertyChanged += this.SortieInfo_PropertyChanged;
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
						this.AddRecord(
							this._currentMapAreaId,
							this._currentMapInfoNo,
							this._currentCellNo,
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
		/// 履歴を1件追加します。UI スレッドで安全に実行します。
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
					this.History.Insert(0, record);

					while (this.History.Count > this._maxHistory)
					{
						this.History.RemoveAt(this.History.Count - 1);
					}
				}));
			}
			else
			{
				this.History.Insert(0, record);
				while (this.History.Count > this._maxHistory)
				{
					this.History.RemoveAt(this.History.Count - 1);
				}
			}
		}

		/// <summary>
		/// 履歴をクリアします。
		/// </summary>
		public void Reset()
		{
			var app = System.Windows.Application.Current;
			if (app != null)
			{
				app.Dispatcher.BeginInvoke((Action)(() => this.History.Clear()));
			}
			else
			{
				this.History.Clear();
			}
		}
	}
}
