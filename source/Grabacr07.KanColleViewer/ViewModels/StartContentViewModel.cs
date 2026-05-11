using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Livet;
using Livet.Commands;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class StartContentViewModel : ViewModel
	{
		public NavigatorViewModel Navigator { get; }


		public bool ClearCacheOnNextStartup
		{
			get => GeneralSettings.ClearCacheOnNextStartup.Value;
			set => GeneralSettings.ClearCacheOnNextStartup.Value = value;
		}


		#region UpdateStatusText 変更通知プロパティ

		private string _UpdateStatusText;

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

		private Uri _UpdateUri;

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

		private ViewModelCommand _CheckForUpdateCommand;

		public ViewModelCommand CheckForUpdateCommand
			=> this._CheckForUpdateCommand ?? (this._CheckForUpdateCommand = new ViewModelCommand(this.CheckForUpdate));

		#endregion


		public StartContentViewModel(NavigatorViewModel navigator)
		{
			this.Navigator = navigator;
		}


		private async void CheckForUpdate()
		{
			this.IsUpdateAvailable = false;
			this.UpdateStatusText = "確認中...";

			try
			{
				var result = await UpdateChecker.CheckAsync();

				if (result.IsUpdateAvailable)
				{
					this.IsUpdateAvailable = true;
					this.UpdateStatusText = $"アップデートがあります ({result.LatestVersion})";

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
					this.UpdateStatusText = "最新版です";
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
				this.UpdateStatusText = "確認に失敗しました";
			}
		}
	}
}
