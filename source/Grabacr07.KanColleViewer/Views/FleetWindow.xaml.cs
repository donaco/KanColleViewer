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
			try
			{
				var logDir = Path.GetDirectoryName(UiLog);
				if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
				{
					Directory.CreateDirectory(logDir);
				}
				File.AppendAllText(UiLog, $"FleetWindow ctor: {DateTime.Now}\n");
			}
			catch (Exception ex)
			{
				// ログ書き込みが失敗しても UI の初期化は継続させる
				Debug.WriteLine($"FleetWindow: failed to append ui log in ctor: {ex}");
			}

			InitializeComponent();

			try
			{
				File.AppendAllText(UiLog, $"FleetWindow InitializeComponent done: {DateTime.Now}\n");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"FleetWindow: failed to append ui log after InitializeComponent: {ex}");
			}
		}
	}
}
