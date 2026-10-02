using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Views
{
	partial class InformationWindow
	{
		public InformationWindow()
		{
			this.InitializeComponent();

			if (Application.Instance is { MainWindow: { } mainWindow })
			{
				mainWindow.Closed += (sender, args) => this.Close();
			}
		}
	}
}
