using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class SlotItemsViewModel : ViewModelBase
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

		public SlotItemsViewModel()
		{
			var itemyard = KanColleClient.Current.Homeport.Itemyard;
			System.ComponentModel.PropertyChangedEventHandler handler = (s, e) => { if (e.PropertyName == nameof(Itemyard.SlotItemsCount)) this.Update(); };
			itemyard.PropertyChanged += handler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => itemyard.PropertyChanged -= handler));
			this.Update();
		}

		private void Update()
		{
			this.Count = KanColleClient.Current.Homeport.Itemyard.SlotItemsCount;
		}
	}
}
