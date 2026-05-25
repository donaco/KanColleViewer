using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Livet.EventListeners;

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
			this.CompositeDisposable.Add(new PropertyChangedEventListener(KanColleClient.Current.Homeport)
			{
				{ nameof(Homeport.Admiral), (sender, args) => this.Update() },
			});
		}

		private void Update()
		{
			this.RaisePropertyChanged(nameof(this.Model));
		}
	}
}
