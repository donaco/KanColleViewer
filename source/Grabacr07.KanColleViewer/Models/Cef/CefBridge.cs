using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using CefSharp;
using CefSharp.Wpf;
using Grabacr07.KanColleViewer.Models.Settings;

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
		private const string CefInitFailedMarkerFileName = "cef-init-failed.marker";

		public static string CachePath => Path.Combine(Application.Instance.LocalAppData.FullName, "Chromium");
		public static string LogFilePath => Path.Combine(CachePath, "cef.log");
		private static string CefInitFailedMarkerPath => Path.Combine(CachePath, CefInitFailedMarkerFileName);

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

		private static bool PrepareForInitializeRetry()
		{
			if (!File.Exists(CefInitFailedMarkerPath)) return false;

			try
			{
				DeleteCefTransientState();
			}
			catch
			{
				// リトライ用のクリーンアップ失敗は初期化本体で再評価する
			}

			return true;
		}

		private static void DeleteCefTransientState()
		{
			TryDeleteDirectory(CachePath);
		}

		private static void MarkInitializeFailure()
		{
			try
			{
				Directory.CreateDirectory(CachePath);
				File.WriteAllText(CefInitFailedMarkerPath, DateTimeOffset.Now.ToString("O"));
			}
			catch
			{
				// マーカー作成失敗は本体の失敗要因ではないため握りつぶす
			}
		}

		private static void ClearInitializeFailureMarker()
		{
			try
			{
				if (File.Exists(CefInitFailedMarkerPath))
				{
					File.Delete(CefInitFailedMarkerPath);
				}
			}
			catch
			{
				// 成功時の後処理失敗は次回起動で吸収する
			}
		}

		private static void TryDeleteDirectory(string path)
		{
			if (!Directory.Exists(path)) return;

			try
			{
				Directory.Delete(path, true);
			}
			catch
			{
				// 他プロセスがロックしている場合は削除できなくても継続
			}
		}

		private static void TryDeleteFile(string path)
		{
			if (!File.Exists(path)) return;

			try
			{
				File.Delete(path);
			}
			catch
			{
				// 他プロセスがロックしている場合は削除できなくても継続
			}
		}

		public static void Initialize()
		{
			lock (cefInitLock)
			{
				try
				{
					EnsureVCRuntimeAvailable();

					// CefSharp はカレントディレクトリからもネイティブ DLL を探すため、
					// デバッグ実行時の作業ディレクトリ不一致に対応する
					Environment.CurrentDirectory = assemblyDirectory;

					if (initialized || (CefSharp.Cef.IsInitialized ?? false)) return;

					var retriedByCleanup = PrepareForInitializeRetry();

					// デバッガアタッチ時のタイミング問題対応
					if (Debugger.IsAttached)
					{
						Thread.Sleep(100);
					}


					// サブプロセス実行ファイルを明示（既定解決の揺らぎを避ける）
					var browserSubprocessPath = Path.Combine(assemblyDirectory, "CefSharp.BrowserSubprocess.exe");
					if (!File.Exists(browserSubprocessPath))
					{
						throw new FileNotFoundException($"CefSharp.BrowserSubprocess.exe not found: '{browserSubprocessPath}'");
					}

					var cefSettings = new CefSettings
					{
						BrowserSubprocessPath = browserSubprocessPath,
					};

					// CefSharp 既定のコマンドライン引数を一旦外し、
					// このアプリで明示したものだけを使って切り分ける
					cefSettings.CefCommandLineArgs.Clear();

					// proxy-server は空でなければ設定する（Network.xaml の設定を残すため）
					var proxyString = Settings.NetworkSettings.LocalProxySettingsString;
					if (!string.IsNullOrWhiteSpace(proxyString))
					{
						cefSettings.CefCommandLineArgs["proxy-server"] = proxyString;
					}

					var cefCommandLineArgsSummary = cefSettings.CefCommandLineArgs.Count == 0
						? "<none>"
						: string.Join("; ", cefSettings.CefCommandLineArgs.Select(x => $"{x.Key}={x.Value}"));

					// ログ設定: デバッグビルドのみ出力、リリースビルドでは無効
#if DEBUG
					Directory.CreateDirectory(CachePath);
					try
					{
						if (File.Exists(LogFilePath)) File.Delete(LogFilePath);
					}
					catch
					{
						// ログ初期化に失敗しても初期化処理自体は継続
					}
					cefSettings.LogSeverity = LogSeverity.Info;
					cefSettings.LogFile = LogFilePath;
#else
					cefSettings.LogSeverity = LogSeverity.Disable;
#endif

					var initializeResult = CefSharp.Cef.Initialize(cefSettings, performDependencyCheck: false, browserProcessHandler: null);

					// 初期化完了を確認
					int waitCount = 0;
					while (!(CefSharp.Cef.IsInitialized ?? false) && waitCount < 150)
					{
						Thread.Sleep(100);
						waitCount++;
					}

					if (!(CefSharp.Cef.IsInitialized ?? false))
					{
						MarkInitializeFailure();
						var libcefPath = Path.Combine(cefDirectory, "libcef.dll");
						var subprocessPath = browserSubprocessPath;
						throw new InvalidOperationException(
							$"Cef initialization failed. Result={initializeResult}, IsInitialized={CefSharp.Cef.IsInitialized}, LogFile='{LogFilePath}', " +
							$"CurrentDirectory='{Environment.CurrentDirectory}', AssemblyDirectory='{assemblyDirectory}', CefDirectory='{cefDirectory}', " +
							$"DLLDirectoryApplied={true}, CEFArgs='{cefCommandLineArgsSummary}', " +
							$"CachePath='{CachePath}', Is64BitProcess={Environment.Is64BitProcess}, libcef.Exists={File.Exists(libcefPath)}('{libcefPath}'), " +
							$"Subprocess.Exists={File.Exists(subprocessPath)}('{subprocessPath}'), RetryCleanupApplied={retriedByCleanup}.");
					}

					ClearInitializeFailureMarker();
					initialized = true;
				}
				catch
				{
					// 初期化時の詳細ログは開発時のみに出す方針のため削除
					throw;
				}
			}
		}

		/// <summary>
		/// CefSharp が必要とする Visual C++ ランタイム (vcruntime140.dll) の存在を確認します。
		/// </summary>
		private static void EnsureVCRuntimeAvailable()
		{
			// CefSharp.Core.Runtime.dll は vcruntime140.dll に依存する
			// 不足時は FileNotFoundException になるが、メッセージが不親切なため事前チェックする
			var runtimeNames = new[] { "vcruntime140.dll", "msvcp140.dll" };

			foreach (var name in runtimeNames)
			{
				var handle = LoadLibrary(name);
				if (handle == IntPtr.Zero)
				{
					throw new DllNotFoundException(
						$"Microsoft Visual C++ 再頒布可能パッケージが見つかりません ({name})。\n\n" +
						"以下の URL からインストールしてください:\n" +
						"https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
						"インストール後、アプリケーションを再起動してください。");
				}
				FreeLibrary(handle);
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr LoadLibrary(string lpFileName);

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool FreeLibrary(IntPtr hModule);

		public static void AttachRequestHandler(ChromiumWebBrowser webBrowser, Action<CapturedHttp> onCaptured)
		{
			webBrowser.RequestHandler = new CustomRequestHandler(onCaptured);
			webBrowser.DownloadHandler = new BlockDownloadHandler();
		}


		/// <summary>
		/// 艦これのゲーム画面が含まれる IFrame を取得します。
		/// IBrowser.GetFrameNames() を使用してフレーム名一覧を取得し、
		/// kcs2/index.php を含む URL のフレームを探します。
		/// </summary>
		public static bool TryGetKanColleCanvas(this ChromiumWebBrowser webBrowser, out IFrame canvas)
		{
			try
			{
				var browser = webBrowser.GetBrowser();
				if (browser == null)
				{
					canvas = null;
					return false;
				}

				// フレーム名一覧からゲームフレームを探す
				var frameNames = browser.GetFrameNames();
				foreach (var name in frameNames)
				{
					var frame = browser.GetFrameByName(name);
					if (frame != null && !string.IsNullOrEmpty(frame.Url) && frame.Url.Contains("/kcs2/"))
					{
						canvas = frame;
						return true;
					}
				}

				// フレーム名で見つからない場合、フレーム識別子で探す
				var frameIds = browser.GetFrameIdentifiers();
				foreach (var frameId in frameIds)
				{
					var frame = browser.GetFrameByIdentifier(frameId);
					if (frame != null && !string.IsNullOrEmpty(frame.Url) && frame.Url.Contains("/kcs2/"))
					{
						canvas = frame;
						return true;
					}
				}

				canvas = null;
				return false;
			}
			catch
			{
				canvas = null;
				return false;
			}
		}
	}
}
