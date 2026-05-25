using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels
{
	public sealed class NullViewModel : ViewModelBase
	{
		public static NullViewModel Instance { get; } = new NullViewModel();
		
		private NullViewModel() { }
	}
}
