using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using Grabacr07.KanColleViewer.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Grabacr07.KanColleViewer.Services
{
	public static class AppNotificationService
	{
		private const string TagKey = "kcv-toast-id";
		private const string DiagnosticLogFileName = "AppNotification.log";
		private static readonly ConcurrentDictionary<string, Action> activatedHandlers = new ConcurrentDictionary<string, Action>();
		private static bool isRegistered;

		public static bool IsAvailable { get; private set; }

		public static AppNotificationSetting? NotificationSetting { get; private set; }

		public static string? InitializationErrorMessage { get; private set; }

		public static string? NotificationSettingErrorMessage { get; private set; }

		public static string? DiagnosticLogWriteErrorMessage { get; private set; }

		private static string DiagnosticLogDirectory =>
			Application.Instance?.LocalAppData.FullName
			?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				ProductInfo.Company,
				ProductInfo.Product);

		public static string DiagnosticLogPath => Path.Combine(DiagnosticLogDirectory, DiagnosticLogFileName);

		public static void Initialize()
		{
			if (isRegistered)
			{
				RefreshNotificationSetting();
				return;
			}

			IsAvailable = false;
			NotificationSetting = null;
			InitializationErrorMessage = null;
			NotificationSettingErrorMessage = null;
			DiagnosticLogWriteErrorMessage = null;

			AppNotificationManager? manager = null;

			try
			{
				manager = AppNotificationManager.Default;
				manager.NotificationInvoked += OnNotificationInvoked;
				manager.Register();
				isRegistered = true;
				IsAvailable = true;
			}
			catch (Exception ex)
			{
				InitializationErrorMessage = GetErrorMessage(ex);
				if (manager != null)
				{
					try
					{
						manager.NotificationInvoked -= OnNotificationInvoked;
					}
					catch (Exception cleanupException)
					{
						RecordDiagnosticError("Initialize cleanup", cleanupException);
					}
				}

				RecordDiagnosticError("Initialize", ex);
				return;
			}

			RefreshNotificationSetting();
		}

		public static void RefreshNotificationSetting()
		{
			if (!isRegistered) return;

			try
			{
				NotificationSetting = AppNotificationManager.Default.Setting;
				NotificationSettingErrorMessage = null;
			}
			catch (Exception ex)
			{
				NotificationSetting = null;
				NotificationSettingErrorMessage = GetErrorMessage(ex);
				RecordDiagnosticError("Read notification setting", ex);
			}
		}

		public static void Shutdown()
		{
			if (!isRegistered) return;

			AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
			AppNotificationManager.Default.Unregister();
			activatedHandlers.Clear();
			isRegistered = false;
			IsAvailable = false;
		}

		public static void Show(string header, string body, Action activated, Action<Exception>? failed)
		{
			if (!IsAvailable) return;

			var id = Guid.NewGuid().ToString("N");
			activatedHandlers[id] = activated;

			try
			{
				var notification = new AppNotificationBuilder()
					.AddArgument(TagKey, id)
					.AddText(header)
					.AddText(body)
					.BuildNotification();

				AppNotificationManager.Default.Show(notification);
			}
			catch (Exception ex)
			{
				activatedHandlers.TryRemove(id, out _);
				failed?.Invoke(ex);
			}
		}

		private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
		{
			var arguments = (IDictionary<string, string>)args.Arguments;
			if (!arguments.ContainsKey(TagKey)) return;
			var id = arguments[TagKey];
			if (!activatedHandlers.TryRemove(id, out var activated)) return;

			Application.Current?.Dispatcher.BeginInvoke(activated);
		}

		private static string GetErrorMessage(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";

		private static void RecordDiagnosticError(string stage, Exception exception)
		{
		#if DEBUG
			const string configuration = "Debug";
			var details = exception.ToString();
		#else
			const string configuration = "Release";
			var details = $"{exception.GetType().Name} (HRESULT: 0x{exception.HResult:X8}): {exception.Message}";
		#endif

			try
			{
				Directory.CreateDirectory(DiagnosticLogDirectory);
				File.AppendAllText(
					DiagnosticLogPath,
					$"[{DateTimeOffset.Now:O}] [{configuration}] {stage}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}",
					new UTF8Encoding(false));
			}
			catch (Exception logException) when (
				logException is IOException
				or UnauthorizedAccessException
				or System.Security.SecurityException
				or ArgumentException
				or NotSupportedException)
			{
				DiagnosticLogWriteErrorMessage = GetErrorMessage(logException);
				System.Diagnostics.Debug.WriteLine($"AppNotificationService: Failed to write diagnostic log: {logException}");
			}
		}
	}
}
