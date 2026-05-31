using System;

namespace Grabacr07.KanColleWrapper.Models
{
	/// <summary>
	/// 航空戦の制空状態 api_kouku.api_stage1.api_disp_seiku
	/// </summary>
	public enum AirSuperiority
	{
		/// <summary>航空戦なし（api_kouku または api_stage1 が null）</summary>
		None = -1,

		/// <summary>航空均衡</summary>
		AirParity = 0,

		/// <summary>制空確保</summary>
		AirSupremacy = 1,

		/// <summary>航空優勢</summary>
		AirSuperior = 2,

		/// <summary>航空劣勢</summary>
		AirInferior = 3,

		/// <summary>制空喪失</summary>
		AirIncapability = 4,
	}

	/// <summary>
	/// 出撃中のマップ位置情報を保持します。
	/// </summary>
	public class SortieInfo : Notifier
	{
		#region MapAreaId 変更通知プロパティ

		private int _MapAreaId;

		/// <summary>
		/// 海域 ID を取得します (例: 6)
		/// </summary>
		public int MapAreaId
		{
			get { return this._MapAreaId; }
			set
			{
				if (this._MapAreaId != value)
				{
					this._MapAreaId = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		#region MapInfoNo 変更通知プロパティ

		private int _MapInfoNo;

		/// <summary>
		/// マップ番号を取得します (例: 2 → 6-2)
		/// </summary>
		public int MapInfoNo
		{
			get { return this._MapInfoNo; }
			set
			{
				if (this._MapInfoNo != value)
				{
					this._MapInfoNo = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		#region CellNo 変更通知プロパティ

		private int? _CellNo;

		/// <summary>
		/// 現在のセル番号を取得します (例: 2 → 6-2-2)
		/// </summary>
		public int? CellNo
		{
			get { return this._CellNo; }
			set
			{
				if (this._CellNo != value)
				{
					this._CellNo = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		#region WinRank 変更通知プロパティ

		private string _WinRank;

		/// <summary>
		/// 戦闘結果のランクを取得します (例: "S", "A", "B" など)
		/// </summary>
		public string WinRank
		{
			get { return this._WinRank; }
			set
			{
				if (this._WinRank != value)
				{
					this._WinRank = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		#region AirResult 変更通知プロパティ

		private AirSuperiority _AirResult = AirSuperiority.None;

		/// <summary>
		/// 航空戦の制空状態を取得します。
		/// </summary>
		public AirSuperiority AirResult
		{
			get { return this._AirResult; }
			set
			{
				if (this._AirResult != value)
				{
					this._AirResult = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		#region IsActive 変更通知プロパティ

		private bool _IsActive;

		/// <summary>
		/// 出撃中かどうかを取得します。
		/// </summary>
		public bool IsActive
		{
			get { return this._IsActive; }
			set
			{
				if (this._IsActive != value)
				{
					this._IsActive = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		#region IsDestruction 変更通知プロパティ

		private bool _IsDestruction;

		/// <summary>
		/// 防空戦が発生したかどうかを示す値を取得します。
		/// </summary>
		public bool IsDestruction
		{
			get { return this._IsDestruction; }
			set
			{
				if (this._IsDestruction != value)
				{
					this._IsDestruction = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		#region IsLdAirbattle 変更通知プロパティ

		private bool _IsLdAirbattle;

		/// <summary>
		/// 航空戦マス（ld_airbattle）かどうかを示す値を取得します。
		/// </summary>
		public bool IsLdAirbattle
		{
			get { return this._IsLdAirbattle; }
			set
			{
				if (this._IsLdAirbattle != value)
				{
					this._IsLdAirbattle = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DisplayText));
				}
			}
		}

		#endregion

		/// <summary>
		/// 表示用テキストを取得します。
		/// </summary>
		public string DisplayText
		{
			get
			{
				if (!this.IsActive) return string.Empty;

				// ベーステキスト: 海域ラベル-マップ番号（例: "E1-1" や "7-4"）
				var baseText = MapCellNameProvider.FormatDisplayKey(this.MapAreaId, this.MapInfoNo);

				// セル番号がある場合のみ追加
				if (this.CellNo.HasValue)
				{
					var cellName = MapCellNameProvider.GetCellName(this.MapAreaId, this.MapInfoNo, this.CellNo.Value);
					baseText += $"-{cellName} ";

					// ボス/帰港判定を追加
					var cellInfo = MapCellNameProvider.GetCellInfo(this.MapAreaId, this.MapInfoNo, this.CellNo.Value);
					if (cellInfo != null)
					{
						if (cellInfo.IsKiko)
						{
							baseText += "[帰港]";
						}
						else if (cellInfo.IsBoss)
						{
							baseText += "[ボス]";
						}
					}
				}

				// 防空戦の表示
				if (this.IsDestruction)
				{
					baseText += "[防空]";
				}

				// 航空戦マス表示
				if (this.IsLdAirbattle)
				{
					baseText += "[航空]";
				}

				// ランクがある場合のみ追加
				if (!string.IsNullOrEmpty(this.WinRank))
				{
					baseText += $"[{this.WinRank}]";
				}

				// 航空戦結果がある場合のみ追加（None 以外）
				if (this.AirResult != AirSuperiority.None)
				{
					baseText += $"[{GetAirSuperiorityText(this.AirResult)}]";
				}

				return baseText;
			}
		}

		/// <summary>
		/// <see cref="AirSuperiority"/> を日本語の短縮表記に変換します。
		/// </summary>
		private static string GetAirSuperiorityText(AirSuperiority value)
		{
			switch (value)
			{
				case AirSuperiority.AirParity: return "均衡";
				case AirSuperiority.AirSupremacy: return "確保";
				case AirSuperiority.AirSuperior: return "優勢";
				case AirSuperiority.AirInferior: return "劣勢";
				case AirSuperiority.AirIncapability: return "喪失";
				default: return string.Empty;
			}
		}

		/// <summary>
		/// 出撃開始時に呼び出します (api_req_map/start)
		/// start取得時は api_no を表示しません
		/// </summary>
		public void Start(int mapAreaId, int mapInfoNo, int cellNo)
		{
			this.MapAreaId = mapAreaId;
			this.MapInfoNo = mapInfoNo;
			this.CellNo = null;
			this.WinRank = null;
			this.AirResult = AirSuperiority.None;
			this.IsDestruction = false;
			this.IsLdAirbattle = false;
			this.IsActive = true;
		}

		/// <summary>
		/// 戦闘開始時に呼び出します (api_req_sortie/battle など)
		/// cellNo を表示開始し、WinRank と AirResult をクリアします
		/// </summary>
		public void EnterBattle(int cellNo)
		{
			this.CellNo = cellNo;
			this.WinRank = null;
			this.AirResult = AirSuperiority.None;
			this.IsDestruction = false;
			this.IsLdAirbattle = false;
		}

		/// <summary>
		/// 航空戦の制空状態を設定します。
		/// battle API のレスポンスから api_kouku.api_stage1.api_disp_seiku を読み取って呼び出します。
		/// </summary>
		public void SetAirResult(AirSuperiority airResult)
		{
			this.AirResult = airResult;
		}

		/// <summary>
		/// 戦闘結果時に呼び出します (api_req_sortie/battleresult)
		/// </summary>
		public void SetBattleResult(string winRank)
		{
			this.WinRank = winRank;
		}

		/// <summary>
		/// 次のセルへ移動時に呼び出します (api_req_map/next)
		/// </summary>
		public void Next(int cellNo)
		{
			this.CellNo = cellNo;
			this.WinRank = null;
			this.AirResult = AirSuperiority.None;
			this.IsDestruction = false;
			this.IsLdAirbattle = false;
		}

		/// <summary>
		/// 防空戦の制空状態を設定します。(api_destruction_battle)
		/// 例: "5-5 [優勢]"
		/// </summary>
		public void SetDestructionAirResult(AirSuperiority airResult)
		{
			this.CellNo = null;
			this.WinRank = null;
			this.AirResult = airResult;
			this.IsDestruction = true;
			this.IsLdAirbattle = false;
		}

		/// <summary>
		/// 母港帰還時に呼び出します (api_port/port)
		/// </summary>
		public void Reset()
		{
			this.MapAreaId = 0;
			this.MapInfoNo = 0;
			this.CellNo = null;
			this.WinRank = null;
			this.AirResult = AirSuperiority.None;
			this.IsDestruction = false;
			this.IsLdAirbattle = false;
			this.IsActive = false;
		}
	}
}
