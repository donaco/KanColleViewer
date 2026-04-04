using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using CefSharp;
using CefSharp.Wpf;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	public static class CefBridge
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool SetDllDirectory(string lpPathName);

		// Assembly.GetExecutingAssembly または AppDomain.CurrentDomain.BaseDirectory を使う
		private static readonly string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
		private static readonly string cefDirectory = ResolveCefDirectory();
		private static bool initialized;
		private static readonly object cefInitLock = new object();

		public static string CachePath => Path.Combine(Application.Instance.LocalAppData.FullName, "Chromium");

		/// <summary>
		/// libcef.dll が存在するディレクトリを解決します。
		/// ビルド構成が x64 の場合、出力ルート直下に配置されるため、
		/// サブフォルダー (x64/x86) → 出力ルート直下の順にフォールバックします。
		/// </summary>
		private static string ResolveCefDirectory()
		{
			var archSubDir = Path.Combine(assemblyDirectory, Environment.Is64BitProcess ? "x64" : "x86");
			if (File.Exists(Path.Combine(archSubDir, "libcef.dll")))
			{
				return archSubDir;
			}

			// x64 ビルド構成では DLL がルート直下に配置される
			if (File.Exists(Path.Combine(assemblyDirectory, "libcef.dll")))
			{
				return assemblyDirectory;
			}

			return archSubDir;
		}

		public static void Initialize()
		{
			lock (cefInitLock)
			{
				try
				{
					// CefSharp はカレントディレクトリからもネイティブ DLL を探すため、
					// デバッグ実行時の作業ディレクトリ不一致に対応する
					Environment.CurrentDirectory = assemblyDirectory;

					SetDllDirectory(cefDirectory);

					if (initialized || (CefSharp.Cef.IsInitialized ?? false)) return;

					// デバッガアタッチ時のタイミング問題対応
					if (System.Diagnostics.Debugger.IsAttached)
					{
						Thread.Sleep(100);
					}

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

					// ログファイルの場所をわかりやすくしておく
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

					// デバッガ環境でも初期化完了を確認
					int waitCount = 0;
					while (!(CefSharp.Cef.IsInitialized ?? false) && waitCount < 50)
					{
						Thread.Sleep(100);
						waitCount++;
					}

					initialized = true;
				}
				catch
				{
					// 初期化時の詳細ログは開発時のみに出す方針のため削除
					throw;
				}
			}
		}

		public static void AttachRequestHandler(ChromiumWebBrowser webBrowser, Action<CapturedHttp> onCaptured)
		{
			webBrowser.RequestHandler = new CustomRequestHandler(onCaptured);
		}

		public static Assembly ResolveCefSharpAssembly(object sender, ResolveEventArgs args)
		{
			if (args.Name.StartsWith("CefSharp"))
			{
				var assemblyName = args.Name.Split(new[] { ',' }, 2).FirstOrDefault() + ".dll";
				var archSpecificPath = Path.Combine(cefDirectory, assemblyName);

				if (File.Exists(archSpecificPath))
					return Assembly.LoadFrom(archSpecificPath);

				// フォールバック: 出力ルート直下を試す
				var rootPath = Path.Combine(assemblyDirectory, assemblyName);
				if (File.Exists(rootPath))
					return Assembly.LoadFrom(rootPath);

				return null;
			}

			return null;
		}

		public static bool TryGetKanColleCanvas(this ChromiumWebBrowser webBrowser, out IFrame canvas)
		{
			var browser = webBrowser.GetBrowser();
			var gameFrame = browser.GetFrameByName("game_frame");
			if (gameFrame == null)
			{
				canvas = null;
				return false;
			}

			canvas = gameFrame;
			return true;
		}
	}
}
