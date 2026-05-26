using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class DialogViewModel : WindowViewModel
	{
		public event EventHandler Accepted;

		public DialogViewModel()
		{
			this.DialogResult = false;
		}

		public void OK()
		{
			this.DialogResult = true;
			this.Accepted?.Invoke(this, EventArgs.Empty);
			this.Close();
		}

		public void Cancel()
		{
			this.DialogResult = false;
			this.Close();
		}
	}
}
