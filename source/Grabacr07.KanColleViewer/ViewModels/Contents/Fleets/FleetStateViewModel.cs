using System;
using System.Collections.Generic;
using System.Linq;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	public class FleetStateViewModel : ViewModelBase
	{
		public FleetState Source { get; }

		public string AverageLevel => this.Source.AverageLevel.ToString("#0.##");

		public string TotalLevel => this.Source.TotalLevel.ToString("###0");

		public string MinAirSuperiorityPotential => this.Source.MinAirSuperiorityPotential.ToString("##0");

		public string MaxAirSuperiorityPotential => this.Source.MaxAirSuperiorityPotential.ToString("##0");

		public string TransportPoint => this.Source.TransportPoint.ToString(this.Source.TransportPoint % 1m == 0m ? "0" : "0.#");

		public string ViewRange => (Math.Floor(this.Source.ViewRange * 100) / 100).ToString("##0.##");

		public string Speed => this.Source.Speed.IsMixed
			? $"速度混成艦隊 ({this.Source.Speed.Min.ToDisplayString()} ～ {this.Source.Speed.Max.ToDisplayString()})"
			: $"{this.Source.Speed.Min.ToDisplayString()}艦隊";

		public HomeportViewModel Homeport { get; }

		public SortieViewModel Sortie { get; }


		public FleetStateViewModel(FleetState source, Fleet fleet = null)
		{
			this.Source = source;
			System.ComponentModel.PropertyChangedEventHandler stateHandler = (s, e) => this.RaisePropertyChanged(e.PropertyName);
			source.PropertyChanged += stateHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => source.PropertyChanged -= stateHandler));

			this.Sortie = new SortieViewModel(source);
			this.CompositeDisposable.Add(this.Sortie);

			this.Homeport = new HomeportViewModel(source, fleet);
			this.CompositeDisposable.Add(this.Homeport);
		}
	}
}
