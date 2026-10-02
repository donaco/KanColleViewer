using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class ShipsViewModel : ViewModelBase
	{
		public int Count
		{
			get => field;
			set
			{
				if (field != value)
				{
					field = value;
					this.RaisePropertyChanged();
				}
			}
		}

		public ShipsViewModel()
		{
			var org = KanColleClient.Current.Homeport.Organization;
			System.ComponentModel.PropertyChangedEventHandler handler = (s, e) => { if (e.PropertyName == nameof(Organization.Ships)) this.Update(); };
			org.PropertyChanged += handler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => org.PropertyChanged -= handler));
			this.Update();
		}

		private void Update()
		{
			this.Count = KanColleClient.Current.Homeport.Organization.Ships.Count;
		}
	}
}
