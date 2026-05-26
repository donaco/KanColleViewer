using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;

namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class ShipyardViewModel : TabItemViewModel
	{
		public override string Name
		{
			get { return "工廠"; }
			protected set { throw new NotImplementedException(); }
		}

		#region RepairingDocks 変更通知プロパティ

		private RepairingDockViewModel[] _RepairingDocks;

		public RepairingDockViewModel[] RepairingDocks
		{
			get { return this._RepairingDocks; }
			set
			{
				if (!Equals(this._RepairingDocks, value))
				{
					this._RepairingDocks = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region BuildingDocks 変更通知プロパティ

		private BuildingDockViewModel[] _BuildingDocks;

		public BuildingDockViewModel[] BuildingDocks
		{
			get { return this._BuildingDocks; }
			set
			{
				if (!Equals(this._BuildingDocks, value))
				{
					this._BuildingDocks = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public CreatedSlotItemViewModel CreatedSlotItem { get; }


		public ShipyardViewModel()
		{
			this.CreatedSlotItem = new CreatedSlotItemViewModel();

			var repairyard = KanColleClient.Current.Homeport.Repairyard;
			System.ComponentModel.PropertyChangedEventHandler repairHandler = (s, e) => { if (e.PropertyName == nameof(Repairyard.Docks)) this.UpdateRepairingDocks(); };
			repairyard.PropertyChanged += repairHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => repairyard.PropertyChanged -= repairHandler));
			this.UpdateRepairingDocks();

			var dockyard = KanColleClient.Current.Homeport.Dockyard;
			System.ComponentModel.PropertyChangedEventHandler dockyardHandler = (s, e) =>
			{
				if (e.PropertyName == nameof(Dockyard.Docks)) this.UpdateBuildingDocks();
				else if (e.PropertyName == nameof(Dockyard.CreatedSlotItem)) this.UpdateSlotItem();
			};
			dockyard.PropertyChanged += dockyardHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => dockyard.PropertyChanged -= dockyardHandler));
			this.UpdateBuildingDocks();
		}


		private void UpdateRepairingDocks()
		{
			this.RepairingDocks = KanColleClient.Current.Homeport.Repairyard.Docks.Select(kvp => new RepairingDockViewModel(kvp.Value)).ToArray();
		}

		private void UpdateBuildingDocks()
		{
			this.BuildingDocks = KanColleClient.Current.Homeport.Dockyard.Docks.Select(kvp => new BuildingDockViewModel(kvp.Value)).ToArray();
		}

		private void UpdateSlotItem()
		{
			this.CreatedSlotItem.Update(KanColleClient.Current.Homeport.Dockyard.CreatedSlotItem);
		}
	}
}
