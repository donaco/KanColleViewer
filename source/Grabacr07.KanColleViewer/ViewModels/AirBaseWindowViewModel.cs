using Grabacr07.KanColleViewer.ViewModels.Contents.AirBases;
using MetroTrilithon.Mvvm;
using System;
using Grabacr07.KanColleViewer.Models.Settings;

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

		// 追加: ウィンドウ個別設定 (TopMost 等)
		public AirBaseWindowSettings Settings { get; }

		public AirBaseWindowViewModel(AirBaseViewModel airBase)
		{
			this.Title = "基地詳細";
			this.source = airBase ?? throw new ArgumentNullException(nameof(airBase));

			// Settings を初期化（TopMost を保持）
			this.Settings = new AirBaseWindowSettings();

			// 表示用に選択オブジェクトをそのまま渡す（参照渡し）
			this.SelectedAirBase = this.source;
		}
	}
}
