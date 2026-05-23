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
		private const int DefaultInitializeWaitLimit = 150;
		private const int DebugInitializeWaitLimit = 220;
		private const int DebugInitializeRetryCount = 3;
		private const int DebugInitializeRetryDelayMilliseconds = 700;

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

		private static void EnsureCefRuntimeFilesAvailable(string browserSubprocessPath)
		{
			var requiredFiles = new[]
			{
				Path.Combine(cefDirectory, "libcef.dll"),
				Path.Combine(cefDirectory, "resources.pak"),
				Path.Combine(cefDirectory, "icudtl.dat"),
				Path.Combine(cefDirectory, "v8_context_snapshot.bin"),
				browserSubprocessPath,
				Path.Combine(assemblyDirectory, "locales", "en-US.pak"),
			};

			var missingFiles = requiredFiles
				.Where(path => !File.Exists(path))
				.ToArray();

			if (missingFiles.Length == 0) return;

			throw new FileNotFoundException(
				"CEF runtime files are missing.\r\n" + string.Join("\r\n", missingFiles),
				missingFiles[0]);
		}

		private static void DeleteCefTransientState()
		{
			TryDeleteDirectory(CachePath);
		}

		private static void DeleteDebugTransientState()
		{
			DeleteCefTransientState();
			TryDeleteFile(CefInitFailedMarkerPath);
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

		private static void AppendInitializeTrace(string message)
		{
#if DEBUG
			try
			{
				Directory.CreateDirectory(CachePath);
				var tracePath = Path.Combine(CachePath, "initialize-trace.log");
				File.AppendAllText(tracePath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
			}
			catch
			{
				// トレース書き込み失敗は初期化本体に影響させない
			}
#endif
		}

		public static void Initialize()
		{
			lock (cefInitLock)
			{
				try
				{
					AppendInitializeTrace("Initialize start");
					EnsureVCRuntimeAvailable();
					AppendInitializeTrace("VC runtime check completed");

					// CefSharp はカレントディレクトリからもネイティブ DLL を探すため、
					// デバッグ実行時の作業ディレクトリ不一致に対応する
					Environment.CurrentDirectory = assemblyDirectory;
					var dllDirectoryApplied = SetDllDirectory(cefDirectory);
					AppendInitializeTrace($"SetDllDirectory applied: {dllDirectoryApplied}");

					if (initialized || (CefSharp.Cef.IsInitialized ?? false))
					{
						AppendInitializeTrace("Initialize skipped: already initialized");
						return;
					}

					var retriedByCleanup = PrepareForInitializeRetry();
					AppendInitializeTrace($"PrepareForInitializeRetry: {retriedByCleanup}");

					// デバッガアタッチ時は前回の痕跡を積極的に除去し、タイミング問題を緩和する
					if (Debugger.IsAttached)
					{
						AppendInitializeTrace("Debugger attached: delete transient state and wait");
						DeleteDebugTransientState();
						Thread.Sleep(700);  // デバッグ時は長めに待機（DLL ロード遅延対応）
					}


					// サブプロセス実行ファイルを明示（既定解決の揺らぎを避ける）
					var browserSubprocessPath = Path.Combine(assemblyDirectory, "CefSharp.BrowserSubprocess.exe");
					EnsureCefRuntimeFilesAvailable(browserSubprocessPath);
					AppendInitializeTrace("CEF runtime files verification completed");

					var cefSettings = new CefSettings
					{
						BrowserSubprocessPath = browserSubprocessPath,
						RootCachePath = CachePath,
					};

					// 既定設定を維持して CefSharp 145 の標準初期化経路を優先する
					var cefCommandLineArgsSummary = "<default>";

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

					// ===== ここから追加 =====
					// CEF 初期化前の詳細ログを出力
					var preInitDebugLog = $@"[PRE-INIT DEBUG] {DateTimeOffset.Now:O}
AssemblyDirectory: {assemblyDirectory}
CefDirectory: {cefDirectory}
CurrentDirectory: {Environment.CurrentDirectory}
BrowserSubprocessPath: {browserSubprocessPath}
BrowserSubprocessPath.Exists: {File.Exists(browserSubprocessPath)}
CachePath: {CachePath}
LogFilePath: {LogFilePath}
Is64BitProcess: {Environment.Is64BitProcess}
CEFArgs: {cefCommandLineArgsSummary}
ResourcesDirPath: <default>
LocalesDirPath: <default>

CEF関連ファイルチェック:
- libcef.dll: {File.Exists(Path.Combine(cefDirectory, "libcef.dll"))}
- resources.pak: {File.Exists(Path.Combine(cefDirectory, "resources.pak"))}
- icudtl.dat: {File.Exists(Path.Combine(cefDirectory, "icudtl.dat"))}
- v8_context_snapshot.bin: {File.Exists(Path.Combine(cefDirectory, "v8_context_snapshot.bin"))}
- locales/ja.pak: {File.Exists(Path.Combine(cefDirectory, "locales", "ja.pak"))}
- locales/en-US.pak: {File.Exists(Path.Combine(cefDirectory, "locales", "en-US.pak"))}
- chrome_100_percent.pak: {File.Exists(Path.Combine(cefDirectory, "chrome_100_percent.pak"))}
- chrome_200_percent.pak: {File.Exists(Path.Combine(cefDirectory, "chrome_200_percent.pak"))}
";
					File.WriteAllText(Path.Combine(CachePath, "pre-init-debug.log"), preInitDebugLog);
					// ===== ここまで追加 =====
#else
					cefSettings.LogSeverity = LogSeverity.Disable;
#endif

AppendInitializeTrace("Calling Cef.Initialize");
var initializeResult = CefSharp.Cef.Initialize(cefSettings, performDependencyCheck: false, browserProcessHandler: null);
AppendInitializeTrace($"Cef.Initialize returned: {initializeResult}, IsInitialized={CefSharp.Cef.IsInitialized}");

					// ===== ここから追加 =====
					// 初期化直後のログ出力
					var postInitDebugLog = $@"[POST-INIT DEBUG] {DateTimeOffset.Now:O}
Initialize Result: {initializeResult}
Cef.IsInitialized: {CefSharp.Cef.IsInitialized}
";
					File.WriteAllText(Path.Combine(CachePath, "post-init-debug.log"), postInitDebugLog);
					// ===== ここまで追加 =====

									// 初期化完了を確認
									int waitCount = 0;
									var initialWaitLimit = Debugger.IsAttached ? DebugInitializeWaitLimit : DefaultInitializeWaitLimit;
									while (!(CefSharp.Cef.IsInitialized ?? false) && waitCount < initialWaitLimit)
									{
										Thread.Sleep(100);
										waitCount++;
									}
									AppendInitializeTrace($"Initial wait completed: waitCount={waitCount}, waitLimit={initialWaitLimit}, IsInitialized={CefSharp.Cef.IsInitialized}");

									if (!(CefSharp.Cef.IsInitialized ?? false))
									{
										if (Debugger.IsAttached && !retriedByCleanup)
										{
											bool? retryResult = null;
											for (var retryAttempt = 1; retryAttempt <= DebugInitializeRetryCount && !(CefSharp.Cef.IsInitialized ?? false); retryAttempt++)
											{
#if DEBUG
												File.WriteAllText(Path.Combine(CachePath, "debug-retry-info.log"),
													$"[DEBUG RETRY] attempt={retryAttempt}/{DebugInitializeRetryCount} wait={DebugInitializeRetryDelayMilliseconds}ms\n");
#endif
												Thread.Sleep(DebugInitializeRetryDelayMilliseconds);
												AppendInitializeTrace($"Calling Cef.Initialize retry attempt={retryAttempt}");
												retryResult = CefSharp.Cef.Initialize(cefSettings, performDependencyCheck: false, browserProcessHandler: null);
												AppendInitializeTrace($"Retry attempt={retryAttempt} result: {retryResult}, IsInitialized={CefSharp.Cef.IsInitialized}");

#if DEBUG
												File.WriteAllText(Path.Combine(CachePath, "post-debug-retry.log"),
													$"[POST-DEBUG-RETRY] {DateTimeOffset.Now:O}\nRetryAttempt: {retryAttempt}\nRetry Initialize Result: {retryResult}\nCef.IsInitialized: {CefSharp.Cef.IsInitialized}\n");
#endif

												waitCount = 0;
												while (!(CefSharp.Cef.IsInitialized ?? false) && waitCount < DebugInitializeWaitLimit)
												{
													Thread.Sleep(100);
													waitCount++;
												}
												AppendInitializeTrace($"Retry wait completed: attempt={retryAttempt}, waitCount={waitCount}, waitLimit={DebugInitializeWaitLimit}, IsInitialized={CefSharp.Cef.IsInitialized}");
											}

											if (!(CefSharp.Cef.IsInitialized ?? false))
											{
												AppendInitializeTrace("Retry path failed");
												MarkInitializeFailure();
												var libcefPath = Path.Combine(cefDirectory, "libcef.dll");
												var subprocessPath = browserSubprocessPath;
												throw new InvalidOperationException(
													$"Cef initialization retry failed. Result={retryResult}, IsInitialized={CefSharp.Cef.IsInitialized}, LogFile='{LogFilePath}', " +
													$"CurrentDirectory='{Environment.CurrentDirectory}', AssemblyDirectory='{assemblyDirectory}', CefDirectory='{cefDirectory}', " +
													$"DLLDirectoryApplied={true}, CEFArgs='{cefCommandLineArgsSummary}', " +
													$"CachePath='{CachePath}', Is64BitProcess={Environment.Is64BitProcess}, libcef.Exists={File.Exists(libcefPath)}('{libcefPath}'), " +
													$"Subprocess.Exists={File.Exists(subprocessPath)}('{subprocessPath}'), RetryCleanupApplied={retriedByCleanup}, RetryCount={DebugInitializeRetryCount}.");
											}
										}
										else
										{
											AppendInitializeTrace("Initial path failed without retry");
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
									}

					ClearInitializeFailureMarker();
					initialized = true;
					AppendInitializeTrace("Initialize completed successfully");
				}
				catch (Exception ex)
				{
					AppendInitializeTrace($"Initialize exception: {ex.GetType().FullName}: {ex.Message}");
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
