using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class AdmiralViewModel : ViewModelBase
	{
		#region Model 変更通知プロパティ

		public Admiral Model => KanColleClient.Current.Homeport.Admiral;

		#endregion

		public AdmiralViewModel()
		{
			var homeport = KanColleClient.Current.Homeport;
			System.ComponentModel.PropertyChangedEventHandler handler = (s, e) => { if (e.PropertyName == nameof(Homeport.Admiral)) this.Update(); };
			homeport.PropertyChanged += handler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => homeport.PropertyChanged -= handler));
		}

		private void Update()
		{
			this.RaisePropertyChanged(nameof(this.Model));
		}
	}
}
