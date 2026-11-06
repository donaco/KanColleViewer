using System;
using System.ComponentModel;
using System.Windows;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Services;
using Microsoft.Windows.AppNotifications;

namespace Grabacr07.KanColleViewer.Views.Settings
{
	partial class Notification
	{
		public Notification()
		{
			this.InitializeComponent();
			this.Loaded += this.HandleLoaded;
			this.Unloaded += this.HandleUnloaded;
		}

		private void HandleLoaded(object sender, RoutedEventArgs e)
		{
			ResourceService.Current.PropertyChanged -= this.HandleResourceChanged;
			ResourceService.Current.PropertyChanged += this.HandleResourceChanged;
			AppNotificationService.RefreshNotificationSetting();
			this.UpdateDiagnostics();
		}

		private void HandleUnloaded(object sender, RoutedEventArgs e)
		{
			ResourceService.Current.PropertyChanged -= this.HandleResourceChanged;
		}

		private void HandleResourceChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(ResourceService.Resources) || e.PropertyName == "Item[]")
			{
				this.UpdateDiagnostics();
			}
		}

		private void UpdateDiagnostics()
		{
			var resources = ResourceService.Current;
			var initializationError = AppNotificationService.InitializationErrorMessage;
			var notificationSetting = AppNotificationService.NotificationSetting;
			var statusResourceKey = initializationError != null
				? "Ui_Notification_Status_InitializationFailed"
				: notificationSetting switch
				{
					AppNotificationSetting.Enabled => "Ui_Notification_Status_Enabled",
					AppNotificationSetting.DisabledForApplication => "Ui_Notification_Status_DisabledForApplication",
					AppNotificationSetting.DisabledForUser => "Ui_Notification_Status_DisabledForUser",
					AppNotificationSetting.DisabledByGroupPolicy => "Ui_Notification_Status_DisabledByGroupPolicy",
					AppNotificationSetting.DisabledByManifest => "Ui_Notification_Status_DisabledByManifest",
					AppNotificationSetting.Unsupported => "Ui_Notification_Status_Unsupported",
					_ => "Ui_Notification_Status_Unknown",
				};

			this.WindowsNotificationStatusText.Text = resources[statusResourceKey];
			this.NotificationInitializationErrorText.Text = initializationError == null
				? string.Empty
				: string.Format(resources["Ui_Notification_Diagnostics_InitializationError"], initializationError);
			this.NotificationInitializationErrorText.Visibility = initializationError == null ? Visibility.Collapsed : Visibility.Visible;

			var settingError = AppNotificationService.NotificationSettingErrorMessage;
			this.NotificationSettingErrorText.Text = settingError == null
				? string.Empty
				: string.Format(resources["Ui_Notification_Diagnostics_SettingError"], settingError);
			this.NotificationSettingErrorText.Visibility = settingError == null ? Visibility.Collapsed : Visibility.Visible;

			var logWriteError = AppNotificationService.DiagnosticLogWriteErrorMessage;
			this.NotificationLogWriteErrorText.Text = logWriteError == null
				? string.Empty
				: string.Format(resources["Ui_Notification_Diagnostics_LogWriteError"], logWriteError);
			this.NotificationLogWriteErrorText.Visibility = logWriteError == null ? Visibility.Collapsed : Visibility.Visible;

			var hasError = initializationError != null || settingError != null || logWriteError != null;
			this.NotificationLogPathText.Text = hasError
				? string.Format(resources["Ui_Notification_Diagnostics_LogPath"], AppNotificationService.DiagnosticLogPath)
				: string.Empty;
			this.NotificationLogPathText.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
		}
	}
}
