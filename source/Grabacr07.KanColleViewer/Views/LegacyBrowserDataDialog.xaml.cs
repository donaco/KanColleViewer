using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Grabacr07.KanColleViewer.Models;

namespace Grabacr07.KanColleViewer.Views
{
	partial class LegacyBrowserDataDialog
	{
		public LegacyBrowserDataDialog()
		{
			this.InitializeComponent();
		}

		private void HandleDeleteClick(object sender, RoutedEventArgs e)
		{
			this.DialogResult = true;
		}

		private void HandleOpenFolderClick(object sender, RoutedEventArgs e)
		{
			e.Handled = true;
			var path = LegacyBrowserData.Current.DataFolder;
			try
			{
				if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
				Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
			}
			catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is UnauthorizedAccessException)
			{
				Debug.WriteLine($"Unable to open legacy browser data folder: {ex}");
				Application.ReportRecoverableException("LegacyBrowserData.OpenFolder", this, ex);
				MessageBox.Show(this,
					string.Format(ResourceService.Current["Ui_Browser_LegacyData_OpenFailed"], path, ex.Message),
					ProductInfo.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
	}
}
