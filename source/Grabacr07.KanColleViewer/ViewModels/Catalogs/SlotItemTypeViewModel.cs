using System;
using Grabacr07.KanColleWrapper.Models;
using Livet;

namespace Grabacr07.KanColleViewer.ViewModels.Catalogs
{
	public class SlotItemTypeViewModel : ViewModel
	{
		public Action SelectionChangedAction { get; set; }

		#region Id 変更通知プロパティ

		private int _Id;

		public int Id
		{
			get { return this._Id; }
			set
			{
				if (this._Id != value)
				{
					this._Id = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region DisplayName 変更通知プロパティ

		private string _DisplayName;

		public string DisplayName
		{
			get { return this._DisplayName; }
			set
			{
				if (this._DisplayName != value)
				{
					this._DisplayName = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsSelected 変更通知プロパティ

		private bool _IsSelected;

		public bool IsSelected
		{
			get { return this._IsSelected; }
			set
			{
				if (this._IsSelected != value)
				{
					this._IsSelected = value;
					this.RaisePropertyChanged();
					if (this.SelectionChangedAction != null) this.SelectionChangedAction();
				}
			}
		}

		#endregion


		public SlotItemTypeViewModel(SlotItemType type)
		{
			this.Id = (int)type;
			this.DisplayName = type.ToString();
		}

		public void Set(bool selected)
		{
			this._IsSelected = selected;
			this.RaisePropertyChanged(nameof(this.IsSelected));
		}
	}
}
