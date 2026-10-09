using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class StartContentViewModel : ViewModelBase
	{
		public NavigatorViewModel? Navigator { get; }


		public bool ClearCacheOnNextStartup
		{
			get => GeneralSettings.ClearCacheOnNextStartup.Value;
			set => GeneralSettings.ClearCacheOnNextStartup.Value = value;
		}


		#region UpdateStatusText 変更通知プロパティ

		private string _UpdateStatusText = string.Empty;
		private string? updateStatusResourceKey;
		private string? updateStatusVersion;

		public string UpdateStatusText
		{
			get { return this._UpdateStatusText; }
			set
			{
				if (this._UpdateStatusText != value)
				{
					this._UpdateStatusText = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsUpdateAvailable 変更通知プロパティ

		private bool _IsUpdateAvailable;

		public bool IsUpdateAvailable
		{
			get { return this._IsUpdateAvailable; }
			set
			{
				if (this._IsUpdateAvailable != value)
				{
					this._IsUpdateAvailable = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region UpdateUri 変更通知プロパティ

		private Uri _UpdateUri = null!;

		public Uri UpdateUri
		{
			get { return this._UpdateUri; }
			set
			{
				if (this._UpdateUri != value)
				{
					this._UpdateUri = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region CheckForUpdateCommand コマンド

		private RelayCommand? _CheckForUpdateCommand;

		public RelayCommand CheckForUpdateCommand
			=> this._CheckForUpdateCommand ??= new RelayCommand(this.CheckForUpdate);

		#endregion


		public StartContentViewModel(NavigatorViewModel? navigator)
		{
			this.Navigator = navigator;
			System.ComponentModel.PropertyChangedEventHandler cultureChangedHandler = (s, e) =>
			{
				if (e.PropertyName == nameof(ResourceService.Resources)) this.RefreshUpdateStatus();
			};
			ResourceService.Current.PropertyChanged += cultureChangedHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => ResourceService.Current.PropertyChanged -= cultureChangedHandler));

			if (GeneralSettings.IsAutoUpdateCheckEnabled.Value)
			{
				this.CheckForUpdate();
			}
		}


		private async void CheckForUpdate()
		{
			this.IsUpdateAvailable = false;
			this.SetUpdateStatus("Ui_UpdateCheck_Status_Checking");

			try
			{
				var result = await UpdateChecker.CheckAsync();

				if (result.IsUpdateAvailable)
				{
					this.IsUpdateAvailable = true;
					this.SetUpdateStatus("Ui_UpdateCheck_Status_Available", result.LatestVersion);

					if (Uri.TryCreate(result.ReleaseUrl, UriKind.Absolute, out var uri)
						&& uri.Scheme == Uri.UriSchemeHttps
						&& uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
					{
						this.UpdateUri = uri;
					}
					else
					{
						Debug.WriteLine($"[UpdateChecker] 不正な ReleaseUrl を破棄しました: {result.ReleaseUrl}");
					}
				}
				else
				{
					this.SetUpdateStatus("Ui_UpdateCheck_Status_UpToDate");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
				this.SetUpdateStatus("Ui_UpdateCheck_Status_Failed");
			}
		}

		private void SetUpdateStatus(string resourceKey, string? version = null)
		{
			this.updateStatusResourceKey = resourceKey;
			this.updateStatusVersion = version;
			this.RefreshUpdateStatus();
		}

		private void RefreshUpdateStatus()
		{
			if (this.updateStatusResourceKey == null) return;

			var format = ResourceService.Current[this.updateStatusResourceKey];
			this.UpdateStatusText = this.updateStatusVersion == null
				? format
				: string.Format(format, this.updateStatusVersion);
		}
	}
}
