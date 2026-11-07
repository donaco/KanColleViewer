using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
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
		private static readonly object lifecycleSync = new object();
		// Windows の通知の既定・最大有効期間に合わせます。
		private static readonly TimeSpan notificationLifetime = TimeSpan.FromDays(3);
		private static readonly TimeSpan handlerCleanupInterval = TimeSpan.FromMinutes(1);
		private static readonly ConcurrentDictionary<string, ActivatedHandler> activatedHandlers = new ConcurrentDictionary<string, ActivatedHandler>();
		private static Timer? expirationTimer;
		private static bool isRegistered;

		private sealed record ActivatedHandler(Action Activated, DateTimeOffset ExpiresAt);

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
			lock (lifecycleSync)
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
					expirationTimer = new Timer(
						static _ => RemoveExpiredHandlers(DateTimeOffset.UtcNow),
						null,
						handlerCleanupInterval,
						handlerCleanupInterval);
					IsAvailable = true;
				}
				catch (Exception ex)
				{
					expirationTimer?.Dispose();
					expirationTimer = null;
					activatedHandlers.Clear();
					InitializationErrorMessage = GetErrorMessage(ex);
					if (manager != null)
					{
						try
						{
							manager.NotificationInvoked -= OnNotificationInvoked;
							if (isRegistered) manager.Unregister();
						}
						catch (Exception cleanupException)
						{
							RecordDiagnosticError("Initialize cleanup", cleanupException);
						}
					}
					isRegistered = false;

					RecordDiagnosticError("Initialize", ex);
					return;
				}

				RefreshNotificationSetting();
			}
		}

		public static void RefreshNotificationSetting()
		{
			lock (lifecycleSync)
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
		}

		public static void Shutdown()
		{
			lock (lifecycleSync)
			{
				IsAvailable = false;
				expirationTimer?.Dispose();
				expirationTimer = null;
				activatedHandlers.Clear();
				if (!isRegistered) return;

				try
				{
					AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
					AppNotificationManager.Default.Unregister();
				}
				finally
				{
					isRegistered = false;
				}
			}
		}

		public static void Show(string header, string body, Action activated, Action<Exception>? failed)
		{
			Exception? failure = null;
			lock (lifecycleSync)
			{
				if (!IsAvailable) return;

				var id = Guid.NewGuid().ToString("N");
				try
				{
					var notification = new AppNotificationBuilder()
						.AddArgument(TagKey, id)
						.AddText(header)
						.AddText(body)
						.BuildNotification();

					var expiresAt = DateTimeOffset.UtcNow.Add(notificationLifetime);
					notification.Expiration = expiresAt;
					activatedHandlers[id] = new ActivatedHandler(activated, expiresAt);
					AppNotificationManager.Default.Show(notification);
				}
				catch (Exception ex)
				{
					activatedHandlers.TryRemove(id, out _);
					failure = ex;
				}
			}
			// 呼び出し元の処理が別スレッドの送信・終了を待つ場合も、ロックを保持しません。
			if (failure != null) failed?.Invoke(failure);
		}

		private static void RemoveExpiredHandlers(DateTimeOffset now)
		{
			foreach (var handler in activatedHandlers)
			{
				if (handler.Value.ExpiresAt <= now)
					activatedHandlers.TryRemove(handler.Key, out _);
			}
		}

		private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
		{
			var arguments = (IDictionary<string, string>)args.Arguments;
			if (!arguments.ContainsKey(TagKey)) return;
			var id = arguments[TagKey];
			if (!activatedHandlers.TryRemove(id, out var handler)) return;
			if (handler.ExpiresAt <= DateTimeOffset.UtcNow) return;

			Application.Current?.Dispatcher.BeginInvoke(handler.Activated);
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
