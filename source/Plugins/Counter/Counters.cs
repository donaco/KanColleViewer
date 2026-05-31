using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;

namespace Counter
{
	public abstract class ObservableObject : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public abstract class CounterBase : ObservableObject, IDisposable
	{
		// ← サブクラスが登録解除処理をここへ積む
		private readonly List<Action> _cleanups = new List<Action>();

		protected void RegisterCleanup(Action cleanup)
		{
			this._cleanups.Add(cleanup);
		}

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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
				}
			}
		}
		#endregion

		#region IsEnabled 変更通知プロパティ
		private bool _IsEnabled = true;
		public bool IsEnabled
		{
			get { return this._IsEnabled; }
			set
			{
				if (this._IsEnabled != value)
				{
					this._IsEnabled = value;
					this.OnPropertyChanged();
				}
			}
		}
		#endregion

		public void Reset()
		{
			this.Count = 0;
		}

		public void Dispose()
		{
			foreach (var cleanup in this._cleanups)
				cleanup();
			this._cleanups.Clear();
		}
	}

	/// <summary>補給をカウント</summary>
	public class SupplyCounter : CounterBase
	{
		public SupplyCounter()
		{
			// ← ハンドラーをフィールドに保持して登録解除できるようにする
			EventHandler handler = (sender, e) =>
			{
				if (!this.IsEnabled) return;
				System.Diagnostics.Debug.WriteLine("[Counter] SupplyCompleted イベント発火!");
				this.Count++;
			};

			KanColleClient.Current.SupplyCompleted += handler;
			this.RegisterCleanup(() => KanColleClient.Current.SupplyCompleted -= handler); // ← 解除を登録

			this.Text = "艦娘に補給した回数";
		}
	}

	/// <summary>装備の破棄をカウント</summary>
	public class ItemDestroyCounter : CounterBase
	{
		public ItemDestroyCounter()
		{
			EventHandler handler = (sender, e) =>
			{
				if (!this.IsEnabled) return;
				System.Diagnostics.Debug.WriteLine("[Counter] ItemDestroyed イベント発火!");
				this.Count++;
			};

			KanColleClient.Current.ItemDestroyed += handler;
			this.RegisterCleanup(() => KanColleClient.Current.ItemDestroyed -= handler); // ← 解除を登録

			this.Text = "装備を破棄した回数";
		}
	}

	/// <summary>遠征の成功をカウント</summary>
	public class MissionCounter : CounterBase
	{
		public MissionCounter()
		{
			EventHandler handler = (sender, e) =>
			{
				if (!this.IsEnabled) return;
				System.Diagnostics.Debug.WriteLine("[Counter] MissionSucceeded イベント発火!");
				this.Count++;
			};

			KanColleClient.Current.MissionSucceeded += handler;
			this.RegisterCleanup(() => KanColleClient.Current.MissionSucceeded -= handler); // ← 解除を登録

			this.Text = "遠征に成功した回数";
		}
	}

	/// <summary>出撃をカウント</summary>
	public class SortieCounter : CounterBase
	{
		public SortieCounter()
		{
			PropertyChangedEventHandler handler = (sender, e) =>
			{
				if (!this.IsEnabled) return;
				if (e.PropertyName == nameof(SortieInfo.IsActive)
					&& KanColleClient.Current.SortieInfo.IsActive)
				{
					System.Diagnostics.Debug.WriteLine("[Counter] 出撃開始検知!");
					this.Count++;
				}
			};

			KanColleClient.Current.SortieInfo.PropertyChanged += handler;
			this.RegisterCleanup(() => KanColleClient.Current.SortieInfo.PropertyChanged -= handler); // ← 解除を登録

			this.Text = "海域に出撃した回数";
		}
	}

	/// <summary>
	/// 出撃履歴の1件分を表すモデルです。
	/// </summary>
	public class SortieRecord : ObservableObject
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
		/// セル番号（生の値）。セルに到達しなかった場合は null
		/// </summary>
		public int? CellNo { get; }

		/// <summary>
		/// 戦闘結果ランク（例: "S"）。
		/// </summary>
		public string WinRank { get; }

		/// <summary>
		/// 括弧付きの戦闘結果表示（例: "[S]"）。WinRank が空なら空文字。
		/// </summary>
		public string WinRankBracketed => string.IsNullOrEmpty(this.WinRank) ? string.Empty : $"[{this.WinRank}]";

		/// <summary>
		/// 航空戦の制空状態（例: AirSuperiority.AirSupremacy）
		/// </summary>
		public AirSuperiority AirResult { get; }

		/// <summary>
		/// 防空戦かどうか
		/// </summary>
		public bool IsDestruction { get; }

		/// <summary>
		/// 陸上基地航空隊による航空支援が行われたかどうか
		/// </summary>
		public bool IsLdAirbattle { get; }

		/// <summary>
		/// 記録日時
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// 海域-セルの内部キー文字列（例: "7-4-C"）。集計に使用します。
		/// ※内部キーは数値ベースで、ラベル変換は行いません。
		/// </summary>
		public string AreaCellKey { get; }

		/// <summary>
		/// 海域表示キー（例: "7-4-K" や "E1-1-3"）。ラベル変換済みの表示用です。
		/// </summary>
		public string CleanAreaCellKey
		{
			get
			{
				return MapCellNameProvider.FormatDisplayKey(this.MapAreaId, this.MapInfoNo, this.CellName);
			}
		}

		/// <summary>
		/// ボス表示テキスト（boss セルなら "[ボス]"、そうでなければ空文字）。
		/// </summary>
		public string BossText
		{
			get
			{
				if (this.CellNo.HasValue)
				{
					var info = MapCellNameProvider.GetCellInfo(this.MapAreaId, this.MapInfoNo, this.CellNo.Value);
					if (info != null && info.IsBoss)
					{
						return "[ボス]";
					}
				}
				return string.Empty;
			}
		}


		/// <summary>
		/// 帰港表示テキスト（帰港セルなら "[帰港]"、そうでなければ空文字）。
		/// </summary>
		public string KikoText
		{
			get
			{
				if (this.CellNo.HasValue
					&& MapCellNameProvider.IsKikoCell(this.MapAreaId, this.MapInfoNo, this.CellNo.Value))
				{
					return "[帰港]";
				}
				return string.Empty;
			}
		}

		/// <summary>
		/// 防空戦　出撃数テキスト（防空戦なら "[防空]"、そうでなければ空文字）。
		/// </summary>
		public string DestructionText
		{
			get { return this.IsDestruction ? "[防空]" : string.Empty; }
		}

		/// <summary>
		/// 航空マス表示テキスト（航空戦マスなら "[航空]"、そうでなければ空文字）。
		/// </summary>
		public string LdAirbattleText
		{
			get { return this.IsLdAirbattle ? "[航空]" : string.Empty; }
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

		public SortieRecord(int mapAreaId, int mapInfoNo, int? cellNo, string winRank, AirSuperiority airResult = AirSuperiority.None, bool isDestruction = false, bool isLdAirbattle = false)
		{
			this.MapAreaId = mapAreaId;
			this.MapInfoNo = mapInfoNo;
			this.CellNo = cellNo;
			this.WinRank = winRank;
			this.AirResult = airResult;
			this.IsDestruction = isDestruction;
			this.IsLdAirbattle = isLdAirbattle;
			this.Timestamp = DateTime.Now;

			if (cellNo.HasValue && cellNo.Value > 0)
			{
				this.CellName = MapCellNameProvider.GetCellName(mapAreaId, mapInfoNo, cellNo.Value);
			}

			// 内部キー（集計用）は数値ベースのまま
			this.AreaCellKey = !string.IsNullOrEmpty(this.CellName)
				? $"{mapAreaId}-{mapInfoNo}-{this.CellName}"
				: $"{mapAreaId}-{mapInfoNo}";

			// 表示テキストはラベル変換済み
			var text = this.CleanAreaCellKey;
			if (!string.IsNullOrEmpty(this.WinRankBracketed))
			{
				text += $" {this.WinRankBracketed}";
			}
			this.DisplayText = text;
		}
	}

	/// <summary>
	/// 海域-セルごとの出撃数と戦闘結果の集計を表すモデルです。
	/// </summary>
	public class SortieAreaCount : ObservableObject
	{
		/// <summary>
		/// 海域-セルの内部キー（例: "7-4-C"）。集計・辞書検索用です。
		/// </summary>
		public string AreaCellKey { get; }

		/// <summary>
		/// 海域ID（例: 7）。ボス判定・ラベル変換に使用
		/// </summary>
		public int? MapAreaId { get; }

		/// <summary>
		/// マップ番号（例: 4）。ボス判定・ラベル変換に使用
		/// </summary>
		public int? MapInfoNo { get; }

		/// <summary>
		/// セル番号（例: 3）。ボス判定に使用
		/// </summary>
		public int? CellNo { get; }

		/// <summary>
		/// セル名（例: "C"）。ラベル変換表示に使用
		/// </summary>
		public string CellName { get; }

		/// <summary>
		/// ラベル変換済みの表示用キー（例: "E1-1-3" や "7-4-C"）
		/// </summary>
		public string DisplayAreaCellKey
		{
			get
			{
				if (this.MapAreaId.HasValue && this.MapInfoNo.HasValue)
				{
					return MapCellNameProvider.FormatDisplayKey(this.MapAreaId.Value, this.MapInfoNo.Value, this.CellName);
				}
				return this.AreaCellKey;
			}
		}

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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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

		/// <summary>
		/// ボス表示テキスト（ボスセルなら "[ボス]"、そうでなければ空文字）
		/// </summary>
		public string BossText
		{
			get
			{
				if (this.MapAreaId.HasValue && this.MapInfoNo.HasValue && this.CellNo.HasValue)
				{
					var info = MapCellNameProvider.GetCellInfo(this.MapAreaId.Value, this.MapInfoNo.Value, this.CellNo.Value);
					if (info != null && info.IsBoss)
					{
						return "[ボス]";
					}
				}
				return string.Empty;
			}
		}

		/// <summary>
		/// 帰港表示テキスト（帰港セルなら "[帰港]"、そうでなければ空文字）
		/// </summary>
		public string KikoText
		{
			get
			{
				if (this.MapAreaId.HasValue && this.MapInfoNo.HasValue && this.CellNo.HasValue
					&& MapCellNameProvider.IsKikoCell(this.MapAreaId.Value, this.MapInfoNo.Value, this.CellNo.Value))
				{
					return "[帰港]";
				}
				return string.Empty;
			}
		}

		#region DestructionCount 変更通知プロパティ

		private int _DestructionCount;

		/// <summary>
		/// 防空戦の回数
		/// </summary>
		public int DestructionCount
		{
			get { return this._DestructionCount; }
			set
			{
				if (this._DestructionCount != value)
				{
					this._DestructionCount = value;
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		/// <summary>
		/// 防空戦表示テキスト（防空戦なら "[防空]"、そうでなければ空文字）。
		/// </summary>
		public string DestructionText => this.DestructionCount > 0 ? "[防空]" : string.Empty;

		public SortieAreaCount(string areaCellKey, int? mapAreaId = null, int? mapInfoNo = null, int? cellNo = null, string cellName = null)
		{
			this.AreaCellKey = areaCellKey;
			this.MapAreaId = mapAreaId;
			this.MapInfoNo = mapInfoNo;
			this.CellNo = cellNo;
			this.CellName = cellName;
			this.Count = 0;
		}

		/// <summary>
		/// 保存データから SortieAreaCount を復元します。
		/// </summary>
		public static SortieAreaCount FromSaveData(CounterDataStore.AreaCountData data)
		{
			var entry = new SortieAreaCount(
				data.AreaCellKey,
				data.MapAreaId,
				data.MapInfoNo,
				data.CellNo,
				data.CellName);

			entry.Count = data.Count;
			entry.SCount = data.SCount;
			entry.ACount = data.ACount;
			entry.BCount = data.BCount;
			entry.AirSupremacyCount = data.AirSupremacyCount;
			entry.AirSuperiorCount = data.AirSuperiorCount;
			entry.DestructionCount = data.DestructionCount;
			entry.LdAirbattleCount = data.LdAirbattleCount;

			// 表示テキストの変更通知を発行
			entry.OnPropertyChanged(nameof(entry.SText));
			entry.OnPropertyChanged(nameof(entry.AText));
			entry.OnPropertyChanged(nameof(entry.BText));
			entry.OnPropertyChanged(nameof(entry.AirSupremacyText));
			entry.OnPropertyChanged(nameof(entry.AirSuperiorText));
			entry.OnPropertyChanged(nameof(entry.DestructionText));
			entry.OnPropertyChanged(nameof(entry.LdAirbattleText));

			return entry;
		}

		#region LdAirbattleCount 変更通知プロパティ

		private int _LdAirbattleCount;

		/// <summary>
		/// 航空戦マスの回数
		/// </summary>
		public int LdAirbattleCount
		{
			get { return this._LdAirbattleCount; }
			set
			{
				if (this._LdAirbattleCount != value)
				{
					this._LdAirbattleCount = value;
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		/// <summary>
		/// 航空マス表示テキスト（航空戦マスなら "[航空]"、そうでなければ空文字）。
		/// </summary>
		public string LdAirbattleText => this.LdAirbattleCount > 0 ? "[航空]" : string.Empty;

		/// <summary>
		/// カウントを 1 増加し、ランク別・制空別の集計も更新します。
		/// </summary>
		/// <param name="winRank">戦闘結果ランク（"S", "A", "B" など）</param>
		/// <param name="airResult">航空戦の制空状態</param>
		/// <param name="isDestruction">防空戦かどうか</param>
		public void Increment(string winRank, AirSuperiority airResult = AirSuperiority.None, bool isDestruction = false, bool isLdAirbattle = false)
		{
			this.Count++;

			switch (winRank)
			{
				case "S":
					this.SCount++;
					this.OnPropertyChanged(nameof(this.SText));
					break;
				case "A":
					this.ACount++;
					this.OnPropertyChanged(nameof(this.AText));
					break;
				case "B":
					this.BCount++;
					this.OnPropertyChanged(nameof(this.BText));
					break;
			}

			switch (airResult)
			{
				case AirSuperiority.AirSupremacy:
					this.AirSupremacyCount++;
					this.OnPropertyChanged(nameof(this.AirSupremacyText));
					break;
				case AirSuperiority.AirSuperior:
					this.AirSuperiorCount++;
					this.OnPropertyChanged(nameof(this.AirSuperiorText));
					break;
			}

			if (isDestruction)
			{
				this.DestructionCount++;
				this.OnPropertyChanged(nameof(this.DestructionText));
			}

			if (isLdAirbattle)
			{
				this.LdAirbattleCount++;
				this.OnPropertyChanged(nameof(this.LdAirbattleText));
			}
		}
	}

	/// <summary>
	/// 直近の出撃履歴を保持・表示するカウンターです。
	/// BossOnly が true の場合、ボスセルでの戦闘結果のみを記録します。
	/// </summary>
	public class SortieHistoryCounter : ObservableObject, IDisposable
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
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
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="maxHistory">保持する履歴の最大件数</param>
		public SortieHistoryCounter(int maxHistory = 20)
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

						// BossOnly フィルター: boss または kiko セルのみ記録
						if (this.BossOnly)
						{
							if (!cellNo.HasValue
								|| !MapCellNameProvider.IsBossCell(this._currentMapAreaId, this._currentMapInfoNo, cellNo.Value))
							{
								break;
							}
						}

						// AirResult を取得して AddRecord に渡す
						this.AddRecord(
							this._currentMapAreaId,
							this._currentMapInfoNo,
							cellNo,
							sortieInfo.WinRank,
							sortieInfo.AirResult,
							isLdAirbattle: sortieInfo.IsLdAirbattle
						);
					}
					break;

				case nameof(SortieInfo.IsDestruction):
					// 防空戦の制空結果を記録
					if (!this.IsEnabled) break;

					if (sortieInfo.IsDestruction
						&& sortieInfo.AirResult != AirSuperiority.None
						&& this._currentMapAreaId > 0
						&& this._currentMapInfoNo > 0)
					{
						this.AddRecord(
							this._currentMapAreaId,
							this._currentMapInfoNo,
							null,       // 防空戦は CellNo なし
							null,       // 防空戦は WinRank なし
							sortieInfo.AirResult,
							isDestruction: true
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
		private void AddRecord(int mapAreaId, int mapInfoNo, int? cellNo, string winRank, AirSuperiority airResult, bool isDestruction = false, bool isLdAirbattle = false)
		{
			try
			{
				SortieRecord record = null;
				try
				{
					record = new SortieRecord(mapAreaId, mapInfoNo, cellNo, winRank, airResult, isDestruction, isLdAirbattle);
				}
				catch (System.Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[SortieHistory] SortieRecord 作成時に例外: {ex}");
					return;
				}

				System.Diagnostics.Debug.WriteLine($"[SortieHistory] 履歴追加: {record.DisplayText}{(isDestruction ? " [防空]" : "")}{(isLdAirbattle ? " [航空]" : "")}");

				var app = System.Windows.Application.Current;
				Action addAndUpdate = () =>
				{
					try
					{
						this.History.Insert(0, record);
						while (this.History.Count > this._maxHistory)
						{
							this.History.RemoveAt(this.History.Count - 1);
						}

						try
						{
							this.UpdateAreaCount(record, winRank, airResult, isDestruction, isLdAirbattle);
						}
						catch (System.Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"[SortieHistory] UpdateAreaCount で例外: {ex}");
						}
					}
					catch (System.Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[SortieHistory] UI 反映時に例外: {ex}");
					}
				};

				if (app != null)
				{
					try
					{
						app.Dispatcher.BeginInvoke((Action)(() => addAndUpdate()));
					}
					catch (System.Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[SortieHistory] Dispatcher 呼び出しで例外: {ex}");
						try { addAndUpdate(); } catch { }
					}
				}
				else
				{
					addAndUpdate();
				}
			}
			catch (System.Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[SortieHistory] AddRecord 全体で例外: {ex}");
			}
		}

		/// <summary>
		/// 海域-セルごとの出撃数とランク別・制空別集計を更新します。
		/// </summary>
		private void UpdateAreaCount(SortieRecord record, string winRank, AirSuperiority airResult, bool isDestruction = false, bool isLdAirbattle = false)
		{
			var areaCellKey = record.AreaCellKey;

			if (this._areaCountMap.TryGetValue(areaCellKey, out var existing))
			{
				existing.Increment(winRank, airResult, isDestruction, isLdAirbattle);
			}
			else
			{
				var newEntry = new SortieAreaCount(
					areaCellKey,
					record.MapAreaId,
					record.MapInfoNo,
					record.CellNo,
					record.CellName);
				newEntry.Increment(winRank, airResult, isDestruction, isLdAirbattle);
				this._areaCountMap[areaCellKey] = newEntry;
				this.AreaCounts.Add(newEntry);
			}

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

		/// <summary>
		/// 保存データから海域ごとの出撃数を復元します。
		/// </summary>
		public void RestoreAreaCounts(List<CounterDataStore.AreaCountData> areaCountDataList)
		{
			if (areaCountDataList == null) return;

			var app = System.Windows.Application.Current;
			Action restoreAction = () =>
			{
				this.AreaCounts.Clear();
				this._areaCountMap.Clear();

				foreach (var data in areaCountDataList)
				{
					var entry = SortieAreaCount.FromSaveData(data);
					this._areaCountMap[entry.AreaCellKey] = entry;
					this.AreaCounts.Add(entry);
				}

				System.Diagnostics.Debug.WriteLine($"[SortieHistory] 出撃数データ復元完了: {this.AreaCounts.Count} 件");
			};

			if (app != null)
			{
				app.Dispatcher.BeginInvoke(restoreAction);
			}
			else
			{
				restoreAction();
			}
		}

		/// <summary>
		/// 保存データから戦闘履歴を復元します。
		/// </summary>
		public void RestoreHistory(List<CounterDataStore.HistoryData> historyDataList)
		{
			if (historyDataList == null) return;

			var app = System.Windows.Application.Current;
			Action restoreAction = () =>
			{
				this.History.Clear();

				foreach (var data in historyDataList)
				{
					var record = new SortieRecord(
						data.MapAreaId,
						data.MapInfoNo,
						data.CellNo,
						data.WinRank,
						(AirSuperiority)data.AirResult,
						data.IsDestruction,
						data.IsLdAirbattle);

					// リフレクションの代わりに、Timestamp プロパティに直接セット
					if (DateTime.TryParse(data.Timestamp, out var ts))
					{
						record.Timestamp = ts;
					}

					this.History.Add(record);
				}

				System.Diagnostics.Debug.WriteLine($"[SortieHistory] 戦闘履歴復元完了: {this.History.Count} 件");
			};

			if (app != null)
			{
				app.Dispatcher.BeginInvoke(restoreAction);
			}
			else
			{
				restoreAction();
			}
		}

		/// <summary>
		/// イベント購読を解除してリソースを解放します。
		/// </summary>
		public void Dispose()
		{
			if (this._sortieInfo != null)
			{
				this._sortieInfo.PropertyChanged -= this.SortieInfo_PropertyChanged;
			}
		}
	}
}
