using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.ViewModels.Contents;
using Grabacr07.KanColleWrapper.Models;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Catalogs
{
	public class ShipViewModel : ViewModelBase
	{
		public int Index { get; }

		public Ship Ship { get; }

		public SallyArea SallyArea { get; }

		public string Speed => this.Ship.Speed.ToDisplayString();

		public string TimeToRepair => this.Ship.TimeToRepair != TimeSpan.Zero
			? $"{(int)this.Ship.TimeToRepair.TotalHours:D2}:{this.Ship.TimeToRepair.ToString(@"mm\:ss")}"
			: "";

		public ShipViewModel(int index, Ship ship, SallyArea? sallyArea)
		{
			this.Index = index;
			this.Ship = ship;
			this.SallyArea = sallyArea ?? SallyArea.Default;

			System.ComponentModel.PropertyChangedEventHandler cultureChangedHandler = (s, e) =>
			{
				if (e.PropertyName == nameof(ResourceService.Resources)) this.RaisePropertyChanged(nameof(this.Speed));
			};
			ResourceService.Current.PropertyChanged += cultureChangedHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => ResourceService.Current.PropertyChanged -= cultureChangedHandler));
		}
	}
}
