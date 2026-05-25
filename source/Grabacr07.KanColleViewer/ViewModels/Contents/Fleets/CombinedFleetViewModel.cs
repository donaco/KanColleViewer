using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
using Grabacr07.KanColleWrapper.Models;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	public class CombinedFleetViewModel : ItemViewModel
	{
		public CombinedFleet Source { get; }

		public string Name => this.Source.Name;

		public FleetStateViewModel State { get; }

		public ViewModelBase QuickStateView => this.Source.State.Situation.HasFlag(FleetSituation.Sortie)
			? this.State.Sortie
			: this.State.Homeport as QuickStateViewViewModel;

		public CombinedFleetViewModel(CombinedFleet fleet)
		{
			this.Source = fleet;

			System.ComponentModel.PropertyChangedEventHandler fleetHandler = (s, a) => { if (a.PropertyName == nameof(fleet.Name)) this.RaisePropertyChanged(nameof(this.Name)); };
			fleet.PropertyChanged += fleetHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => fleet.PropertyChanged -= fleetHandler));

			System.ComponentModel.PropertyChangedEventHandler stateHandler = (s, a) => { if (a.PropertyName == nameof(fleet.State.Situation)) this.RaisePropertyChanged(nameof(this.QuickStateView)); };
			fleet.State.PropertyChanged += stateHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => fleet.State.PropertyChanged -= stateHandler));

			this.State = new FleetStateViewModel(fleet.State);
			this.CompositeDisposable.Add(this.State);
		}
	}
}
