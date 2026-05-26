using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class ShipViewModel : ViewModelBase
	{
		public Ship Ship { get; }

		public ShipViewModel(Ship ship)
		{
			this.Ship = ship;
		}
	}
}
