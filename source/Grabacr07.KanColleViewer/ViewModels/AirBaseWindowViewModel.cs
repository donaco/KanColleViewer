using Grabacr07.KanColleViewer.ViewModels.Contents.AirBases;
using MetroTrilithon.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Grabacr07.KanColleViewer.Models.Settings;
using Livet.Commands;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class AirBaseWindowViewModel : WindowViewModel
	{
		private AirBasesViewModel source;

		#region AirBases 変更通知プロパティ

		private ObservableCollection<AirBaseViewModel> _AirBases;

		public ObservableCollection<AirBaseViewModel> AirBases
		{
			get { return this._AirBases; }
			set
			{
				if (this._AirBases != value)
				{
					this._AirBases = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

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

		// ウィンドウ個別設定 (TopMost 等)
		public AirBaseWindowSettings Settings { get; }

		// 基地詳細ボタンコマンド
		public ICommand ShowAirBaseWindowCommand { get; }

		public AirBaseWindowViewModel(AirBasesViewModel airBasesVM)
		{
			this.Title = "基地詳細";
			this.source = airBasesVM ?? throw new ArgumentNullException(nameof(airBasesVM));

			// Settings を初期化（TopMost を保持）
			this.Settings = new AirBaseWindowSettings();

			// AirBases と SelectedAirBase をバインド（配列をObservableCollectionに変換）
			this.AirBases = new ObservableCollection<AirBaseViewModel>(airBasesVM.AirBases ?? Enumerable.Empty<AirBaseViewModel>());

			// 初期選択: ウィンドウを開いた時点の選択状態をコピー（以後は連動しない）
			this.SelectedAirBase = airBasesVM.SelectedAirBase;

			// 基地詳細ボタンコマンド
			this.ShowAirBaseWindowCommand = new ViewModelCommand(() =>
			{
				if (this.SelectedAirBase != null)
				{
					this.source.ShowAirBaseWindow();
				}
			});

			// SourceのプロパティChangedをサブスクライブ
			// AirBases の更新のみ監視し、SelectedAirBase は連動させない
			this.source.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(AirBasesViewModel.AirBases))
				{
					// 現在の選択を保持
					var currentSelectedAreaId = this.SelectedAirBase?.AreaId;

					this.AirBases = new ObservableCollection<AirBaseViewModel>(airBasesVM.AirBases ?? Enumerable.Empty<AirBaseViewModel>());

					// AirBases が更新された場合、同じ AreaId の項目を再選択（なければ先頭）
					if (currentSelectedAreaId.HasValue)
					{
						this.SelectedAirBase = this.AirBases.FirstOrDefault(x => x.AreaId == currentSelectedAreaId.Value)
											?? this.AirBases.FirstOrDefault();
					}
					else
					{
						this.SelectedAirBase = this.AirBases.FirstOrDefault();
					}
				}
				// SelectedAirBase の変更は監視しない（連動させない）
			};
		}
	}
}
