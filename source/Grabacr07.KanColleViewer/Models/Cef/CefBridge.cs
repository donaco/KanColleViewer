using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Wpf;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	public static class CefBridge
	{
		// Assembly.GetExecutingAssembly または AppDomain.CurrentDomain.BaseDirectory を使う
		private static readonly string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
		private static readonly string cefDirectory = Path.Combine(assemblyDirectory, Environment.Is64BitProcess ? "x64" : "x86");
		private static bool initialized;
		private static readonly object cefInitLock = new object();

		public static string CachePath => Path.Combine(Application.Instance.LocalAppData.FullName, "Chromium");

		public static void Initialize()
		{
			lock (cefInitLock)
			{
				if (initialized || CefSharp.Cef.IsInitialized) return;

				CefSharpSettings.SubprocessExitIfParentProcessClosed = true;

				// 結合する前にパスを決定して存在確認する
				var browserSubprocessPath = Path.Combine(cefDirectory, "CefSharp.BrowserSubprocess.exe");
				// フォールバック: ルート出力ディレクトリにも存在するか試す
				var fallbackPath = Path.Combine(assemblyDirectory, "CefSharp.BrowserSubprocess.exe");

				if (!File.Exists(browserSubprocessPath) && File.Exists(fallbackPath))
				{
					browserSubprocessPath = fallbackPath;
				}

				if (!File.Exists(browserSubprocessPath))
				{
					throw new FileNotFoundException(
						$"CefSettings.BrowserSubprocessPath not found. Tried: '{browserSubprocessPath}' and '{fallbackPath}'. " +
						"Ensure CefSharp.BrowserSubprocess.exe and native dependencies are copied to the output folder (x86/x64).");
				}

				var cefSettings = new CefSettings
				{
					BrowserSubprocessPath = browserSubprocessPath,
					CachePath = CefBridge.CachePath,
				};

				cefSettings.CefCommandLineArgs["disable-features"] = "AudioServiceOutOfProcess";
				cefSettings.CefCommandLineArgs["proxy-server"] = Settings.NetworkSettings.LocalProxySettingsString;

				CefSharpSettings.SubprocessExitIfParentProcessClosed = true;
				CefSharp.Cef.Initialize(cefSettings);

				initialized = true;
			}
		}

		public static Assembly ResolveCefSharpAssembly(object sender, ResolveEventArgs args)
		{
			if (args.Name.StartsWith("CefSharp"))
			{
				var assemblyName = args.Name.Split(new[] { ',' }, 2).FirstOrDefault() + ".dll";
				var archSpecificPath = Path.Combine(cefDirectory, assemblyName);

				return File.Exists(archSpecificPath)
					? Assembly.LoadFile(archSpecificPath)
					: null;
			}

			return null;
		}

		public static bool TryGetKanColleCanvas(this ChromiumWebBrowser webBrowser, out IFrame canvas)
		{
			var browser = webBrowser.GetBrowser();
			var gameFrame = browser.GetFrame("game_frame");
			if (gameFrame == null)
			{
				canvas = null;
				return false;
			}

			canvas = browser.GetFrameIdentifiers()
				.Select(x => browser.GetFrame(x))
				.Where(x => x.Parent?.Identifier == gameFrame.Identifier)
				.FirstOrDefault(x => x.Url.Contains("/kcs2/index.php"));

			return canvas != null;
		}
	}
}
