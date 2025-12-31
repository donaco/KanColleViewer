using Grabacr07.KanColleViewer.ViewModels.Contents.Fleets;
using Grabacr07.KanColleWrapper;
using Livet.Messaging;
using MetroTrilithon.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class FleetWindowViewModel : WindowViewModel
	{
		private FleetViewModel[] allFleets;

		#region Fleets 変更通知プロパティ

		private ItemViewModel[] _Fleets;

		public ItemViewModel[] Fleets
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

		private ItemViewModel _SelectedFleet;

		/// <summary>
		/// 現在選択されている艦隊を取得または設定します。
		/// </summary>
		public ItemViewModel SelectedFleet
		{
			get { return this._SelectedFleet; }
			set
			{
				if (this._SelectedFleet != value)
				{
					if (this._SelectedFleet != null) this._SelectedFleet.IsSelected = false;
					if (value != null) value.IsSelected = true;

					this._SelectedFleet = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion


		public FleetWindowViewModel()
		{
			this.Title = "艦隊詳細";
			this.Fleets = new ItemViewModel[0];

			try
			{
				if (KanColleClient.Current?.Homeport?.Organization == null)
				{
					System.Diagnostics.Debug.WriteLine("Organization is null in FleetWindowViewModel constructor.");
					return;
				}

				KanColleClient.Current.Homeport.Organization
					.Subscribe(nameof(Organization.Fleets), this.InitializeFleets)
					.Subscribe(nameof(Organization.Combined), this.UpdateFleets)
					.Subscribe(nameof(Organization.CombinedFleet), this.UpdateFleets)
					.AddTo(this);

				this.InitializeFleets();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in FleetWindowViewModel constructor: {ex}");
			}
		}


		private void InitializeFleets()
		{
			this.allFleets = KanColleClient.Current.Homeport.Organization.Fleets.Select(kvp => new FleetViewModel(kvp.Value)).ToArray();
			this.UpdateFleets();
		}

		#region 艦隊表示の更新
		private void UpdateFleets()
		{
			// 現在の選択を一時保存（Fleet の ID が取れればそれを使う）
			int? previousSelectedFleetId = null;
			if (this.SelectedFleet is FleetViewModel fvm) previousSelectedFleetId = fvm.Id;
			else if (this.SelectedFleet is ItemViewModel ivm && ivm is FleetViewModel fv) previousSelectedFleetId = fv.Id;

			// ややこしいけど、CombinedFleetViewModel は連合艦隊が編成・解除される度に使い捨て
			// FleetViewModel は InitializeFleets() で作ったインスタンスをずっと使う

			foreach (var f in this.Fleets.OfType<CombinedFleetViewModel>()) f.Dispose();

			if (KanColleClient.Current.Homeport.Organization.Combined)
			{
				var cfvm = new CombinedFleetViewModel(KanColleClient.Current.Homeport.Organization.CombinedFleet);
				var fleets = this.allFleets.Where(x => cfvm.Source.Fleets.All(f => f != x.Source));

				this.Fleets = EnumerableEx.Return<ItemViewModel>(cfvm).Concat(fleets).ToArray();

				// 以前選択していた艦隊があれば復元（連合艦隊中の個別艦隊が選択されていた場合）
				if (previousSelectedFleetId.HasValue)
				{
					var candidate = this.Fleets.OfType<FleetViewModel>().FirstOrDefault(x => x.Id == previousSelectedFleetId.Value);
					this.SelectedFleet = (ItemViewModel)(candidate ?? (ItemViewModel)cfvm);
				}
				else
				{
					this.SelectedFleet = cfvm;
				}
			}
			else
			{
				this.Fleets = this.allFleets.OfType<ItemViewModel>().ToArray();

				// 以前選択していた艦隊を復元できれば復元、できなければ先頭を選択
				if (previousSelectedFleetId.HasValue)
				{
					this.SelectedFleet = this.Fleets.OfType<FleetViewModel>().FirstOrDefault(x => x.Id == previousSelectedFleetId.Value) ?? this.Fleets.FirstOrDefault();
				}
				else
				{
					// 既存のロジックの互換性確保
					if (this.allFleets.All(x => x != this.SelectedFleet))
					{
						this.SelectedFleet = this.Fleets.FirstOrDefault();
					}
				}
			}
		}
		#endregion
	}
}
