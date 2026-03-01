using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Livet;

namespace Counter
{
	public class CounterViewModel : ViewModel
	{
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
	}
}
