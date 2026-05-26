using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels
{
	public class ItemViewModel : ViewModelBase
	{
		#region IsSelected 変更通知プロパティ

		private bool _IsSelected;

		public virtual bool IsSelected
		{
			get { return this._IsSelected; }
			set
			{
				if (this._IsSelected != value)
				{
					this._IsSelected = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion
	}
}