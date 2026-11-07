using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Models.WebView2;

namespace Grabacr07.KanColleViewer.Models
{
	public sealed class LegacyBrowserData : ObservableObject
	{
		public static LegacyBrowserData Current { get; } = new LegacyBrowserData();

		public string DataFolder { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			ProductInfo.Company, ProductInfo.Product, "Chromium");

		public string LegacyPluginDataFolder { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"terry_u16");

		private bool webView2ExistedOnStartup;
		private bool deleteOnStartup;
		private bool isDeletionAvailable;

		public bool IsDeletionAvailable
		{
			get => this.isDeletionAvailable;
			private set => this.SetProperty(ref this.isDeletionAvailable, value);
		}

		private LegacyBrowserData() { }

		private string[] DeletionTargets => new[]
		{
			this.DataFolder,
			Providers.LegacyRoamingCompanyDirectoryPath,
			this.LegacyPluginDataFolder,
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "terry_u16"),
		};

		private bool HasLegacyData => this.DeletionTargets.Any(Directory.Exists);

		internal void Prepare()
		{
			this.webView2ExistedOnStartup = Directory.Exists(WebView2Setup.UserDataFolder);
			this.IsDeletionAvailable = this.webView2ExistedOnStartup && this.HasLegacyData;
			// Capture only the saved reservation, not changes made during this session.
			this.deleteOnStartup = GeneralSettings.DeleteLegacyBrowserDataOnNextStartup.Value;
		}

		internal async Task DeleteOnStartupAsync()
		{
			if (!this.deleteOnStartup) return;
			this.deleteOnStartup = false;
			GeneralSettings.DeleteLegacyBrowserDataOnNextStartup.Value = false;
			this.IsDeletionAvailable = false;
			var deletionPath = this.DataFolder;
			try
			{
				if (!this.webView2ExistedOnStartup || !Directory.Exists(WebView2Setup.UserDataFolder))
				{
					throw new IOException(ResourceService.Current["Ui_Browser_LegacyData_WebView2Missing"]);
				}
				await Task.Run(() =>
				{
					foreach (var path in this.DeletionTargets)
					{
						deletionPath = path;
						try
						{
							Directory.Delete(path, recursive: true);
						}
						catch (DirectoryNotFoundException) when (!Directory.Exists(path))
						{
							// Already removed; continue with the remaining target.
						}
					}
				});
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Debug.WriteLine($"Legacy browser data deletion failed: {ex}");
				Application.ReportRecoverableException("Startup.LegacyBrowserData", this, ex);
				MessageBox.Show(
					string.Format(ResourceService.Current["Ui_Browser_LegacyData_DeleteFailed"], deletionPath, ex.Message),
					ProductInfo.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
			}
			finally
			{
				this.IsDeletionAvailable = this.webView2ExistedOnStartup && this.HasLegacyData;
			}
		}
	}
}
