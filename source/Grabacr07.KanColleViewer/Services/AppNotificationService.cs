using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Grabacr07.KanColleViewer.Services
{
	public static class AppNotificationService
	{
		private const string TagKey = "kcv-toast-id";
		private static readonly ConcurrentDictionary<string, Action> activatedHandlers = new ConcurrentDictionary<string, Action>();
		private static bool isRegistered;

		public static bool IsAvailable { get; private set; }

		public static void Initialize()
		{
			if (isRegistered) return;

			try
			{
				AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
				AppNotificationManager.Default.Register();
				isRegistered = true;
				IsAvailable = true;
			}
			catch
			{
				AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
				IsAvailable = false;
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

		public static void Show(string header, string body, Action activated, Action<Exception> failed)
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
	}
}
