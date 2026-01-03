using Grabacr07.KanColleViewer.ViewModels.Contents.AirBases;
using MetroTrilithon.Mvvm;
using System;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class AirBaseWindowViewModel : WindowViewModel
	{
		private readonly AirBaseViewModel source;

		#region SelectedAirBase 変更通知プロパティ

		private AirBaseViewModel _SelectedAirBase;

		public AirBaseViewModel SelectedAirBase
		{
			get { return this._SelectedAirBase; }
			set
			{
				if (this._SelectedAirBase != value)
				{
					this._SelectedAirBase = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public AirBaseWindowViewModel(AirBaseViewModel airBase)
		{
			this.Title = "基地詳細";
			this.source = airBase ?? throw new ArgumentNullException(nameof(airBase));

			// 表示用に選択オブジェクトをそのまま渡す（参照渡し）
			this.SelectedAirBase = this.source;
		}
	}
}
