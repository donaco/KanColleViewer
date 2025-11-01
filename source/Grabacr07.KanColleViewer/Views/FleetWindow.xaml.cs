using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Grabacr07.KanColleViewer.Views
{
	public partial class FleetWindow
	{
		private static readonly string UiLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KanColleViewer", "logs", "ui.log");

		public FleetWindow()
		{
			File.AppendAllText(UiLog, $"FleetWindow ctor: {DateTime.Now}\n");
			InitializeComponent();
			File.AppendAllText(UiLog, $"FleetWindow InitializeComponent done: {DateTime.Now}\n");
		}
	}
}
