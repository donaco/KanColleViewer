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
		#region Count 変更通知プロパティ

		private int _Count;

		public int Count
		{
			get { return this._Count; }
			set
			{
				if (this._Count != value)
				{
					this._Count = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

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
