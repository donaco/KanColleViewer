using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;

namespace Grabacr07.KanColleViewer.Views.Settings
{
	partial class Browser
	{
		public Browser()
		{
			this.InitializeComponent();
		}

		private void HandleDeleteLegacyBrowserDataClick(object sender, RoutedEventArgs e)
		{
			var checkBox = (CheckBox)sender;
			var reserveDeletion = false;
			if (checkBox.IsChecked == true && LegacyBrowserData.Current.IsDeletionAvailable)
			{
				var dialog = new LegacyBrowserDataDialog { Owner = System.Windows.Window.GetWindow(this) };
				reserveDeletion = dialog.ShowDialog() == true;
			}
			GeneralSettings.DeleteLegacyBrowserDataOnNextStartup.Value = reserveDeletion;
			checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, reserveDeletion);
		}
	}
}
