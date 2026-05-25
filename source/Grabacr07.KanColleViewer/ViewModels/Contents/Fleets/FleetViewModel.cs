using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
using Grabacr07.KanColleWrapper.Models;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	/// <summary>
	/// 単一の艦隊情報を提供します。
	/// </summary>
	public class FleetViewModel : ItemViewModel
	{
		public Fleet Source { get; }

		public int Id => this.Source.Id;

		public string Name => string.IsNullOrEmpty(this.Source.Name.Trim()) ? "(第 " + this.Source.Id + " 艦隊)" : this.Source.Name;

		/// <summary>
		/// 艦隊に所属している艦娘のコレクションを取得します。
		/// </summary>
		public ShipViewModel[] Ships
		{
			get { return this.Source.Ships.Select(x => new ShipViewModel(x)).ToArray(); }
		}

		public FleetStateViewModel State { get; }

		public ExpeditionViewModel Expedition { get; }

		public ViewModelBase QuickStateView
		{
			get
			{
				var situation = this.Source.State.Situation;
				if (situation == FleetSituation.Empty)
				{
					return NullViewModel.Instance;
				}
				if (situation.HasFlag(FleetSituation.Sortie))
				{
					return this.State.Sortie;
				}
				if (situation.HasFlag(FleetSituation.Expedition))
				{
					return this.Expedition;
				}

				return this.State.Homeport;
			}
		}


		public FleetViewModel(Fleet fleet)
		{
			this.Source = fleet;

			System.ComponentModel.PropertyChangedEventHandler fleetHandler = (s, a) => this.RaisePropertyChanged(a.PropertyName);
			fleet.PropertyChanged += fleetHandler;
			this.CompositeDisposable.Add(new Grabacr07.KanColleViewer.Infrastructure.Lifetime.DelegateDisposable(() => fleet.PropertyChanged -= fleetHandler));

			System.ComponentModel.PropertyChangedEventHandler stateHandler = (s, a) => { if (a.PropertyName == nameof(fleet.State.Situation)) this.RaisePropertyChanged(nameof(this.QuickStateView)); };
			fleet.State.PropertyChanged += stateHandler;
			this.CompositeDisposable.Add(new Grabacr07.KanColleViewer.Infrastructure.Lifetime.DelegateDisposable(() => fleet.State.PropertyChanged -= stateHandler));

			this.State = new FleetStateViewModel(fleet.State);
			this.CompositeDisposable.Add(this.State);

			this.Expedition = new ExpeditionViewModel(fleet.Expedition);
			this.CompositeDisposable.Add(this.Expedition);
		}
	}
}
