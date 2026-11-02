using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Models.Network;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleWrapper;
using Microsoft.Web.WebView2.Core;

namespace Grabacr07.KanColleViewer.Models.WebView2
{
	public enum WebView2FrameRateMode
	{
		Default,
		Unlimited,
	}

	public static class WebView2Migration
	{
		public static bool IsEnabled { get; } = Environment.GetCommandLineArgs()
			.Any(argument => string.Equals(argument, "--webview2", StringComparison.OrdinalIgnoreCase));

		public static string UserDataFolder => Path.Combine(
			Application.Instance?.LocalAppData.FullName
				?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Grabacr07", "KanColleViewer"),
			"WebView2");

		public static string RuntimeVersion { get; private set; } = string.Empty;
		public static bool IsDevToolsEnabled { get; private set; }
		public static bool ClearCacheOnStartup { get; private set; }
		private static CoreWebView2EnvironmentOptions? options;
		private static Task<CoreWebView2Environment>? environmentTask;

		public static void Prepare()
		{
			try
			{
				RuntimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
			}
			catch (WebView2RuntimeNotFoundException ex)
			{
				throw new InvalidOperationException("Evergreen WebView2 Runtimeが見つかりません。https://developer.microsoft.com/microsoft-edge/webview2/ から導入してください。自動インストールやCEFへの切替は行いません。", ex);
			}

			var arguments = new List<string>();
			switch (GeneralSettings.WebView2FrameRate.Value)
			{
				case WebView2FrameRateMode.Default:
					break;
				case WebView2FrameRateMode.Unlimited:
					arguments.Add("--disable-frame-rate-limit");
					break;
				default:
					throw new InvalidOperationException("WebView2のFPS設定値が不正です。");
			}
			if (GeneralSettings.IsGpuDisabled.Value) arguments.Add("--disable-gpu");
			if (NetworkSettings.Relay.IsEnabled.Value)
			{
				RelayHttpProxyService.StartForWebView2();
				arguments.Add($"--proxy-server=127.0.0.1:{RelayHttpProxyService.ListeningPort}");
			}
			else if (!GeneralSettings.IsProxyMode.Value)
			{
				switch (NetworkSettings.Proxy.Type.Value)
				{
					case ProxyType.DirectAccess:
						arguments.Add("--no-proxy-server");
						break;
					case ProxyType.SystemProxy:
						break;
					case ProxyType.SpecificProxy:
						var proxy = NetworkSettings.LocalProxySettingsString;
						if (string.IsNullOrWhiteSpace(proxy) || proxy.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '"'))
						{
							throw new InvalidOperationException("指定プロキシの設定が不正です。直接通信には切り替えません。");
						}
						arguments.Add($"--proxy-server=\"{proxy}\"");
						break;
					default:
						throw new InvalidOperationException("プロキシの設定値が不正です。");
				}
			}

			options = new CoreWebView2EnvironmentOptions
			{
				AdditionalBrowserArguments = string.Join(" ", arguments),
			};
			IsDevToolsEnabled = GeneralSettings.IsRemoteDebuggingEnabled.Value;
			ClearCacheOnStartup = GeneralSettings.ClearCacheOnNextStartup.Value;
		}

		public static Task<CoreWebView2Environment> GetEnvironmentAsync()
		{
			if (options is null) throw new InvalidOperationException("WebView2の起動設定が準備されていません。");
			Directory.CreateDirectory(UserDataFolder);
			return environmentTask ??= CoreWebView2Environment.CreateAsync(null, UserDataFolder, options);
		}
	}
}
