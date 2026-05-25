using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	public class ExpeditionViewModel : ViewModelBase
	{
		private readonly Expedition source;

		public Mission Mission => this.source.Mission;

		public bool IsInExecution => this.source.IsInExecution;

		public string ReturnTime => this.source.ReturnTime?.LocalDateTime.ToString("MM/dd HH:mm") ?? "--/-- --:--";

		public string Remaining => this.source.Remaining.HasValue
			? $"{(int)this.source.Remaining.Value.TotalHours:D2}:{this.source.Remaining.Value.ToString(@"mm\:ss")}"
			: "--:--:--";

		public ExpeditionViewModel(Expedition expedition)
		{
			this.source = expedition;
			System.ComponentModel.PropertyChangedEventHandler handler = (sender, args) => this.RaisePropertyChanged(args.PropertyName);
			expedition.PropertyChanged += handler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => expedition.PropertyChanged -= handler));
		}
	}
}
