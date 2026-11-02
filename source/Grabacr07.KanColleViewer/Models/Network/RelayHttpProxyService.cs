using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleWrapper.Net;

namespace Grabacr07.KanColleViewer.Models.Network
{
	internal static class RelayHttpProxyService
	{
		private static readonly object SyncRoot = new();
		private static RelayHttpProxy? relayProxy;
		private static long lastFailureNotificationTicks;

		internal static bool IsRunning
		{
			get
			{
				lock (SyncRoot) return relayProxy is not null;
			}
		}

		internal static int ListeningPort
		{
			get
			{
				lock (SyncRoot)
				{
					return relayProxy?.ListeningPort
						?? throw new InvalidOperationException("内蔵中継プロキシは起動していません。");
				}
			}
		}

		internal static void StartForWebView2()
		{
			if (!NetworkSettings.Relay.IsEnabled.Value)
			{
				Stop();
				return;
			}

			if (string.IsNullOrWhiteSpace(NetworkSettings.Relay.UpstreamHost.Value)
				|| NetworkSettings.Relay.UpstreamPort.Value == 0)
			{
				throw new InvalidOperationException("内蔵中継プロキシの上流ホストまたはポートが未設定です。直接接続へは切り替えません。");
			}

			Start();
		}

		private static void Start()
		{
			lock (SyncRoot)
			{
				if (relayProxy is not null) return;

				var proxy = new RelayHttpProxy
				{
					UpstreamHost = NetworkSettings.Relay.UpstreamHost.Value,
					UpstreamPort = NetworkSettings.Relay.UpstreamPort.Value,
					AllowDirectFallback = false,
				};
				proxy.UpstreamConnectionFailed += HandleUpstreamConnectionFailed;

				try
				{
					proxy.Start(0);
					relayProxy = proxy;
				}
				catch (Exception ex)
				{
					proxy.UpstreamConnectionFailed -= HandleUpstreamConnectionFailed;
					proxy.Dispose();
					throw new InvalidOperationException("内蔵中継プロキシを起動できません。設定を確認してください。直接接続へは切り替えません。", ex);
				}
			}
		}

		internal static void Stop()
		{
			lock (SyncRoot)
			{
				if (relayProxy is null) return;

				relayProxy.UpstreamConnectionFailed -= HandleUpstreamConnectionFailed;
				relayProxy.Dispose();
				relayProxy = null;
			}
		}

		private static void HandleUpstreamConnectionFailed(object? sender, EventArgs e)
		{
			var now = DateTime.UtcNow.Ticks;
			var throttleInterval = TimeSpan.FromSeconds(15).Ticks;
			while (true)
			{
				var previous = Interlocked.Read(ref lastFailureNotificationTicks);
				if (now - previous < throttleInterval) return;
				if (Interlocked.CompareExchange(ref lastFailureNotificationTicks, now, previous) == previous) break;
			}

			const string message = "中継先プロキシに接続できません。設定を確認してください。直接接続へは切り替えていません。";
			Debug.WriteLine(message);

			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;

			void Notify()
			{
				if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
					StatusService.Current.Notify(message);
			}

			if (dispatcher.CheckAccess()) Notify();
			else dispatcher.BeginInvoke((Action)Notify);
		}
	}
}
