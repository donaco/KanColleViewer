using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Properties;
using MetroTrilithon.Mvvm;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Settings
{
	public class ScreenshotSettingsViewModel : ViewModelBase
	{
		#region CanOpenDestination 変更通知プロパティ

		private bool _CanOpenDestination;

		public bool CanOpenDestination
		{
			get { return this._CanOpenDestination; }
			set
			{
				if (this._CanOpenDestination != value)
				{
					this._CanOpenDestination = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public ScreenshotSettingsViewModel()
		{
			ScreenshotSettings.Destination
				.Subscribe(x => this.CanOpenDestination = Directory.Exists(x))
				.AddTo(this);
		}

		public void OpenDestinationSelectionDialog()
		{
			using (var dialog = new FolderBrowserDialog())
			{
				dialog.Description = Resources.Settings_Screenshot_FolderSelectionDialog_Title;
				dialog.SelectedPath = this.CanOpenDestination ? ScreenshotSettings.Destination : "";

				if (dialog.ShowDialog() == DialogResult.OK)
				{
					var selectedPath = dialog.SelectedPath;
					if (Directory.Exists(selectedPath))
					{
						ScreenshotSettings.Destination.Value = selectedPath;
					}
				}
			}
		}

		public void OpenScreenshotFolder()
		{
			if (!this.CanOpenDestination) return;

			try
			{
				Process.Start(ScreenshotSettings.Destination);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}
	}
}
