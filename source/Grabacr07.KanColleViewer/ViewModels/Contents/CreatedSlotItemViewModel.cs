using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class CreatedSlotItemViewModel : ViewModelBase
	{
		public bool? Succeed
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

		public string Name
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

		public CreatedSlotItemViewModel()
		{
			this.Succeed = null;
			this.Name = "-----";
		}

		public void Update(CreatedSlotItem item)
		{
			// Null 安全: item または SlotItemInfo が null の場合は既定値を設定する
			if (item == null)
			{
				this.Succeed = null;
				this.Name = "-----";
				return;
			}

			this.Succeed = item.Succeed;
			this.Name = item.SlotItemInfo?.Name ?? "（不明）";
		}
	}
}
