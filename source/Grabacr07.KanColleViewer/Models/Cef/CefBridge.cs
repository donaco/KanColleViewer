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

				// 例: リモートデバッグポートを追加（開発用）
				cefSettings.CefCommandLineArgs["remote-debugging-port"] = "9222";

				// ログファイルの場所をわかりやすくしておく（既に LogFile を設定している場合は上書きしない）
				try
				{
					var cefLogDir = Path.Combine(CefBridge.CachePath);
					Directory.CreateDirectory(cefLogDir);
					cefSettings.LogFile = Path.Combine(cefLogDir, "cef.log");
				}
				catch { }

				// proxy-server は空でなければ設定する（Network.xaml の設定を残すため）
				var proxyString = Settings.NetworkSettings.LocalProxySettingsString;
				if (!string.IsNullOrWhiteSpace(proxyString))
				{
					cefSettings.CefCommandLineArgs["proxy-server"] = proxyString;
				}
				// ログレベルを上げてログファイルを指定 （トラブルシューティング用）
				cefSettings.LogSeverity = LogSeverity.Verbose;
				cefSettings.LogFile = Path.Combine(CachePath, "cef.log");

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

		// 追加: ChromiumWebBrowser に RequestHandler を割り当てるユーティリティ
		public static void AttachRequestHandler(ChromiumWebBrowser browser, Action<CapturedHttp> onCaptured)
		{
			if (browser == null) throw new ArgumentNullException(nameof(browser));
			browser.RequestHandler = new CustomRequestHandler(onCaptured);
		}
	}
}
