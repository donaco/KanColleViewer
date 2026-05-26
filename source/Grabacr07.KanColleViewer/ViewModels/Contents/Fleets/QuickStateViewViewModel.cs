using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents.Fleets
{
	public abstract class QuickStateViewViewModel : ViewModelBase
	{
		public FleetState State { get; }

		protected QuickStateViewViewModel(FleetState state)
		{
			this.State = state;
		}
	}
}
