using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Views;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Livet.Messaging;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;
using StatefulModel;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	public class FleetsViewModel : TabItemViewModel
	{
		private MultipleDisposable fleetListeners;

		// 艦隊詳細ウィンドウのインスタンスを保持
		private static Window fleetWindowInstance;

		public override string Name
		{
			get { return Properties.Resources.Fleets; }
			protected set { throw new NotImplementedException(); }
		}

		#region Fleets 変更通知プロパティ

		private FleetViewModel[] _Fleets;

		public FleetViewModel[] Fleets
		{
			get { return this._Fleets; }
			set
			{
				if (this._Fleets != value)
				{
					this._Fleets = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SelectedFleet 変更通知プロパティ

		private FleetViewModel _SelectedFleet;

		/// <summary>
		/// 現在選択されている艦隊を取得または設定します。
		/// </summary>
		public FleetViewModel SelectedFleet
		{
			get { return this._SelectedFleet; }
			set
			{
				if (this._SelectedFleet != value)
				{
					if (this._SelectedFleet != null) this.SelectedFleet.IsSelected = false;
					if (value != null) value.IsSelected = true;
					this._SelectedFleet = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public FleetsViewModel()
		{
			KanColleClient.Current.Homeport.Organization
				.Subscribe(nameof(Organization.Fleets), this.UpdateFleets)
				.AddTo(this);
			Disposable
				.Create(() => this.fleetListeners?.Dispose())
				.AddTo(this);
		}

		#region 艦隊ウィンドウを安全に表示
		/// <summary>
		///	null チェックと例外処理を追加して、艦隊ウィンドウを安全に表示
		///	既に開いている場合はアクティブにする
		///	</summary>
		public void ShowFleetWindow()
		{
			try
			{
				// null チェックを追加
				if (KanColleClient.Current?.Homeport?.Organization == null)
				{
					System.Diagnostics.Debug.WriteLine("Organization is null when trying to show fleet window.");
					return;
				}

				// 既存のウィンドウがあり、閉じられていない場合はアクティブにする
				if (fleetWindowInstance != null && fleetWindowInstance.IsLoaded)
				{
					fleetWindowInstance.Activate();
					if (fleetWindowInstance.WindowState == WindowState.Minimized)
					{
						fleetWindowInstance.WindowState = WindowState.Normal;
					}
					return;
				}

				// 新しいウィンドウを作成
				var vm = new FleetWindowViewModel();
				var window = new FleetWindow { DataContext = vm };

				// ウィンドウが閉じられたらインスタンスをクリア
				window.Closed += (s, e) => fleetWindowInstance = null;

				fleetWindowInstance = window;
				window.Show();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in ShowFleetWindow: {ex}");
			}
		}
		#endregion

		private void UpdateFleets()
		{
			// 選択を一時保存（ID ベース）
			var previousSelectedId = this.SelectedFleet?.Id;

			this.fleetListeners?.Dispose();
			this.fleetListeners = new MultipleDisposable();

			this.Fleets = KanColleClient.Current.Homeport.Organization.Fleets
				.Select(kvp => this.ToViewModel(kvp.Value))
				.ToArray();

			// 可能なら以前選択されていた艦隊を復元、それが無ければ先頭を選択
			if (previousSelectedId.HasValue)
			{
				this.SelectedFleet = this.Fleets.FirstOrDefault(f => f.Id == previousSelectedId.Value) ?? this.Fleets.FirstOrDefault();
			}
			else
			{
				this.SelectedFleet = this.Fleets.FirstOrDefault();
			}
		}

		private FleetViewModel ToViewModel(Fleet fleet)
		{
			var vm = new FleetViewModel(fleet).AddTo(this.fleetListeners);
			fleet.Subscribe(nameof(Fleet.ShipsUpdated), () => { if (KanColleSettings.AutoFleetSelectWhenShipsChanged) this.SelectedFleet = vm; }, false).AddTo(this.fleetListeners);
			fleet.Subscribe(nameof(Fleet.IsInSortie), () => { if (KanColleSettings.AutoFleetSelectWhenSortie) this.SelectedFleet = vm; }, false).AddTo(this.fleetListeners);

			return vm;
		}
	}
}
