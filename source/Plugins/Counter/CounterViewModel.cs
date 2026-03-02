using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Livet;

namespace Counter
{
	public class CounterViewModel : ViewModel
	{
		/// <summary>
		/// ポップアップウィンドウのインスタンスを保持します（多重起動防止用）。
		/// </summary>
		private CounterWindow _popupWindow;

		#region Counters 変更通知プロパティ

		private ObservableCollection<CounterBase> _Counters;

		public ObservableCollection<CounterBase> Counters
		{
			get { return this._Counters; }
			set
			{
				if (this._Counters != value)
				{
					this._Counters = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SortieHistory 変更通知プロパティ

		private SortieHistoryCounter _SortieHistory;

		/// <summary>
		/// 出撃履歴カウンター
		/// </summary>
		public SortieHistoryCounter SortieHistory
		{
			get { return this._SortieHistory; }
			set
			{
				if (this._SortieHistory != value)
				{
					this._SortieHistory = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsCounterEnabled 変更通知プロパティ

		private bool _IsCounterEnabled = true;

		/// <summary>
		/// カウンター（第1列）の有効/無効を切り替えます。
		/// </summary>
		public bool IsCounterEnabled
		{
			get { return this._IsCounterEnabled; }
			set
			{
				if (this._IsCounterEnabled != value)
				{
					this._IsCounterEnabled = value;
					this.RaisePropertyChanged();

					// 各カウンターの有効/無効を連動
					if (this.Counters != null)
					{
						foreach (var counter in this.Counters)
						{
							counter.IsEnabled = value;
						}
					}
				}
			}
		}

		#endregion

		#region IsSortieHistoryEnabled 変更通知プロパティ

		private bool _IsSortieHistoryEnabled = true;

		/// <summary>
		/// 戦闘履歴・出撃数（第2・3列）の有効/無効を切り替えます。
		/// </summary>
		public bool IsSortieHistoryEnabled
		{
			get { return this._IsSortieHistoryEnabled; }
			set
			{
				if (this._IsSortieHistoryEnabled != value)
				{
					this._IsSortieHistoryEnabled = value;
					this.RaisePropertyChanged();

					// SortieHistoryCounter の有効/無効を連動
					if (this.SortieHistory != null)
					{
						this.SortieHistory.IsEnabled = value;
					}
				}
			}
		}

		#endregion

		#region IsTopMost 変更通知プロパティ

		private bool _IsTopMost = true;

		/// <summary>
		/// ポップアップウィンドウを最前面に固定するかどうかを切り替えます。
		/// </summary>
		public bool IsTopMost
		{
			get { return this._IsTopMost; }
			set
			{
				if (this._IsTopMost != value)
				{
					this._IsTopMost = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		/// <summary>
		/// ポップアップウィンドウを開きます。
		/// 既に開いている場合はアクティブにします。
		/// </summary>
		public void OpenPopupWindow()
		{
			try
			{
				if (this._popupWindow != null && this._popupWindow.IsLoaded)
				{
					this._popupWindow.Activate();
					return;
				}

				this._popupWindow = new CounterWindow
				{
					DataContext = this,
				};

				this._popupWindow.Closed += (s, e) => this._popupWindow = null;
				this._popupWindow.Show();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Counter] ポップアップウィンドウの表示に失敗: {ex.Message}");
				this._popupWindow = null;
			}
		}

		/// <summary>
		/// ポップアップウィンドウを安全に閉じます。
		/// アプリケーション終了時に呼び出されます。
		/// </summary>
		public void ClosePopupWindow()
		{
			try
			{
				if (this._popupWindow != null && this._popupWindow.IsLoaded)
				{
					this._popupWindow.Close();
				}
			}
			catch
			{
				// 終了時の例外は無視
			}
			finally
			{
				this._popupWindow = null;
			}
		}
	}
}
