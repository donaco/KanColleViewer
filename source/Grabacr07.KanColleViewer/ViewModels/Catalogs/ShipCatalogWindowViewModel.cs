using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels.Catalogs
{
	public class ShipCatalogWindowViewModel : WindowViewModel
	{
		private readonly Subject<Unit> updateSource = new Subject<Unit>();
		private readonly Homeport homeport = KanColleClient.Current.Homeport;
		private SallyArea[] sallyAreas;

		private IReadOnlyCollection<int> selectedShipTypeIds = Array.Empty<int>();
		private IReadOnlyCollection<int> selectedDaiNaiShipIds = Array.Empty<int>();

		public ShipCatalogWindowSettings Settings { get; }

		public ShipCatalogSortWorker SortWorker { get; }
		public IReadOnlyCollection<ShipTypeViewModel> ShipTypes { get; }

		public ShipLevelFilter ShipLevelFilter { get; }
		public ShipLockFilter ShipLockFilter { get; }
		public ShipSpeedFilter ShipSpeedFilter { get; }
		public ShipModernizeFilter ShipModernizeFilter { get; }
		public ShipRemodelingFilter ShipRemodelingFilter { get; }
		public ShipExpeditionFilter ShipExpeditionFilter { get; }
		public ShipSallyAreaFilter ShipSallyAreaFilter { get; }
		public ShipDamagedFilter ShipDamagedFilter { get; }
		public ShipConditionFilter ShipConditionFilter { get; }

		public bool CheckAllShipTypes
		{
			get { return this.ShipTypes.All(x => x.IsSelected); }
			set
			{
				foreach (var type in this.ShipTypes) type.Set(value);
				this.Update();
			}
		}

		#region Ships 変更通知プロパティ

		private IReadOnlyCollection<ShipViewModel> _Ships;

		public IReadOnlyCollection<ShipViewModel> Ships
		{
			get { return this._Ships; }
			set
			{
				if (this._Ships != value)
				{
					this._Ships = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsOpenFilterSettings 変更通知プロパティ

		private bool _IsOpenFilterSettings;

		public bool IsOpenFilterSettings
		{
			get { return this._IsOpenFilterSettings; }
			set
			{
				if (this._IsOpenFilterSettings != value)
				{
					this._IsOpenFilterSettings = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsOpenSortSettings 変更通知プロパティ

		private bool _IsOpenSortSettings;

		public bool IsOpenSortSettings
		{
			get { return this._IsOpenSortSettings; }
			set
			{
				if (this._IsOpenSortSettings != value)
				{
					this._IsOpenSortSettings = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsReloading 変更通知プロパティ

		private bool _IsReloading;

		public bool IsReloading
		{
			get { return this._IsReloading; }
			set
			{
				if (this._IsReloading != value)
				{
					this._IsReloading = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public ShipCatalogWindowViewModel()
		{
			this.Title = "所属艦娘一覧";
			this.IsOpenFilterSettings = true;
			this.Settings = new ShipCatalogWindowSettings();

			this.SortWorker = new ShipCatalogSortWorker();

			this.ShipTypes = KanColleClient.Current.Master.ShipTypes
				.Where(kvp => !(kvp.Value.Id == 15 && kvp.Value.Name == "補給艦")) // おそらく敵艦用と思われる補給艦を除外
				.Select(kvp => new ShipTypeViewModel(kvp.Value)
				{
					IsSelected = true,
					SelectionChangedAction = () => this.Update()
				})
				.ToList();

			this.ShipLevelFilter = new ShipLevelFilter(this.Update);
			this.ShipLockFilter = new ShipLockFilter(this.Update);
			this.ShipSpeedFilter = new ShipSpeedFilter(this.Update);
			this.ShipModernizeFilter = new ShipModernizeFilter(this.Update);
			this.ShipRemodelingFilter = new ShipRemodelingFilter(this.Update);
			this.ShipExpeditionFilter = new ShipExpeditionFilter(this.Update);
			this.ShipSallyAreaFilter = new ShipSallyAreaFilter(this.Update);
			this.ShipDamagedFilter = new ShipDamagedFilter(this.Update);
			this.ShipConditionFilter = new ShipConditionFilter(this.Update);

			this.updateSource
				.Do(_ => this.IsReloading = true)
				.SelectMany(_ => this.GetSallyAreaAsync())
				.SelectMany(x => this.UpdateAsync(x))
				.Do(_ => this.IsReloading = false)
				.Subscribe()
				.AddTo(this);

			this.homeport.Organization
				.Subscribe(nameof(Organization.Ships), this.Update)
				.AddTo(this);
		}

		public void Update()
		{
			this.ShipExpeditionFilter.SetFleets(this.homeport.Organization.Fleets);

			// 通常のチェックボックス操作では ShipType の現在状態を反映する
			// DaiNai ボタン操作中は、そのボタン条件を優先する
			if (this.daiNaiFilterMode == DaiNaiFilterMode.None)
			{
				this.selectedShipTypeIds = this.ShipTypes
					.Where(x => x.IsSelected)
					.Select(x => x.Id)
					.ToArray();
			}

			this.RaisePropertyChanged(nameof(this.CheckAllShipTypes));
			this.updateSource.OnNext(Unit.Default);
		}

		public void ResetDaiNaiFilter()
		{
			this.selectedShipTypeIds = Array.Empty<int>();
			this.selectedDaiNaiShipIds = Array.Empty<int>();
			this.daiNaiFilterMode = DaiNaiFilterMode.None;

			foreach (var type in this.ShipTypes)
			{
				type.Set(false);
			}

			this.Update();
		}

		private enum DaiNaiFilterMode
		{
			None,
			ShipIdOnly,
			ShipTypeOrShipId,
		}

		private DaiNaiFilterMode daiNaiFilterMode = DaiNaiFilterMode.None;

		private void UpdateSelection(int[] shipTypeIds, Func<DaiNaiShipEntry, bool> predicate, DaiNaiFilterMode mode)
		{
			this.selectedShipTypeIds = shipTypeIds ?? Array.Empty<int>();
			this.selectedDaiNaiShipIds = predicate == null
				? Array.Empty<int>()
				: DaiNaiShipProvider.GetShipIds(predicate).Distinct().ToArray();

			this.daiNaiFilterMode = mode;

			foreach (var type in this.ShipTypes)
			{
				type.Set(this.selectedShipTypeIds.Contains(type.Id));
			}

			this.Update();
		}
		private static readonly HashSet<int> NaikExcludedShipIds = new HashSet<int>
			//除外する艦娘の shipid を指定。現状は大発と同じ艦娘を除外している。
			{
			163,402,
			};

		private IObservable<Unit> UpdateAsync(SallyArea[] areas)
		{
			return Observable.Start(() =>
			{
				var hasSelectedShipTypes = this.selectedShipTypeIds.Any();
				var hasSelectedDaiNaiShips = this.selectedDaiNaiShipIds.Any();

				var list = this.homeport.Organization.Ships
					.Select(kvp => kvp.Value)
					.Where(x => x != null)
					.Where(x =>
					{
						switch (this.daiNaiFilterMode)
						{
							case DaiNaiFilterMode.ShipIdOnly:
								// 大発: shipid のみ
								return this.selectedDaiNaiShipIds.Contains(x.Info.Id);

							case DaiNaiFilterMode.ShipTypeOrShipId:
								// 内火艇: shiptype OR shipid
								return !NaikExcludedShipIds.Contains(x.Info.Id)
									&& (
										(hasSelectedShipTypes && this.selectedShipTypeIds.Contains(x.Info.ShipType.Id))
										|| (hasSelectedDaiNaiShips && this.selectedDaiNaiShipIds.Contains(x.Info.Id))
									);

							default:
								// 通常ボタン: shiptype のみ
								return hasSelectedShipTypes && this.selectedShipTypeIds.Contains(x.Info.ShipType.Id);
						}
					})
					.Where(this.ShipLevelFilter.Predicate)
					.Where(this.ShipLockFilter.Predicate)
					.Where(this.ShipSpeedFilter.Predicate)
					.Where(this.ShipModernizeFilter.Predicate)
					.Where(this.ShipRemodelingFilter.Predicate)
					.Where(this.ShipExpeditionFilter.Predicate)
					.Where(this.ShipSallyAreaFilter.Predicate)
					.Where(this.ShipDamagedFilter.Predicate)
					.Where(this.ShipConditionFilter.Predicate);

				this.Ships = this.SortWorker.Sort(list)
					.Select((x, i) => new ShipViewModel(i + 1, x, areas.FirstOrDefault(y => y.Area == x.SallyArea)))
					.ToList();
			});
		}

		private IObservable<SallyArea[]> GetSallyAreaAsync()
		{
			return this.sallyAreas == null
				? SallyArea.GetAsync()
					.ToObservable()
					.Do(x =>
					{
						// これはひどい
						this.sallyAreas = x;
						this.ShipSallyAreaFilter.SetSallyArea(x);
					})
				: Observable.Return(this.sallyAreas);
		}

		public void SetShipType(int[] ids)
		{
			this.selectedShipTypeIds = ids ?? Array.Empty<int>();
			this.daiNaiFilterMode = DaiNaiFilterMode.None;
			this.selectedDaiNaiShipIds = Array.Empty<int>();

			foreach (var type in this.ShipTypes)
			{
				type.Set(this.selectedShipTypeIds.Contains(type.Id));
			}

			this.Update();
		}

		public void SetDaih()
		{
			// 大発は shipid のみ
			this.UpdateSelection(null, x => x.Daih, DaiNaiFilterMode.ShipIdOnly);
		}

		public void SetNaik(int[] shipTypeIds)
		{
			// 内火艇は shiptype OR shipid
			this.UpdateSelection(shipTypeIds, x => x.Naik, DaiNaiFilterMode.ShipTypeOrShipId);
		}
	}
}
