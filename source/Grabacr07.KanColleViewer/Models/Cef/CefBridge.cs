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

		private static string FallbackLocalAppData => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Grabacr07", "KanColleViewer");

		public static string CachePath => Path.Combine(
			Application.Instance?.LocalAppData?.FullName ?? FallbackLocalAppData,
			"Chromium");
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

		/// <summary>
		/// CefSettings を毎回新しいインスタンスとして生成します。
		/// Cef.Initialize() は内部で using(settings) を実行して settings を Dispose するため、
		/// 同一インスタンスを再利用すると settings.settings が null になり NullReferenceException が発生します。
		/// </summary>
		private static CefSettings CreateCefSettings(string browserSubprocessPath)
		{
			var settings = new CefSettings
			{
				BrowserSubprocessPath = browserSubprocessPath,
				RootCachePath = CachePath,
				ResourcesDirPath = cefDirectory,
				LocalesDirPath = Path.Combine(assemblyDirectory, "locales"),
				LogSeverity = LogSeverity.Disable,
				LogFile = LogFilePath,
			};
			return settings;
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

					// PrepareNativePaths() で設定済みだが、Initialize() 単独呼び出しにも対応する
					Environment.CurrentDirectory = assemblyDirectory;
					var dllDirectoryApplied = SetDllDirectory(cefDirectory);
					AppendInitializeTrace($"SetDllDirectory applied: {dllDirectoryApplied}");

					if (initialized || (CefSharp.Cef.IsInitialized ?? false))
					{
						AppendInitializeTrace("Initialize skipped: already initialized");
						return;
					}

					if (!Debugger.IsAttached)
					{
						PrepareForInitializeRetry();
					}
					else
					{
						AppendInitializeTrace("Debugger attached: preserving CEF transient state");
					}

					var browserSubprocessPath = Path.Combine(assemblyDirectory, "CefSharp.BrowserSubprocess.exe");
					EnsureCefRuntimeFilesAvailable(browserSubprocessPath);
					AppendInitializeTrace("CEF runtime files verification completed");

					Directory.CreateDirectory(CachePath);

					// 診断ログ: Release/Debug 両方で出力（問題調査用）
					var diagLog = $@"[CEF INIT DIAG] {DateTimeOffset.Now:O}
AssemblyDirectory : {assemblyDirectory}
CefDirectory      : {cefDirectory}
CurrentDirectory  : {Environment.CurrentDirectory}
SubprocessPath    : {browserSubprocessPath} (Exists={File.Exists(browserSubprocessPath)})
CachePath         : {CachePath}
Is64BitProcess    : {Environment.Is64BitProcess}
libcef.dll        : {File.Exists(Path.Combine(cefDirectory, "libcef.dll"))}
resources.pak     : {File.Exists(Path.Combine(cefDirectory, "resources.pak"))}
icudtl.dat        : {File.Exists(Path.Combine(cefDirectory, "icudtl.dat"))}
v8_context_snapshot.bin: {File.Exists(Path.Combine(cefDirectory, "v8_context_snapshot.bin"))}
locales/en-US.pak : {File.Exists(Path.Combine(assemblyDirectory, "locales", "en-US.pak"))}
";
					try { File.WriteAllText(Path.Combine(CachePath, "cef-init-diag.log"), diagLog); } catch { }
					AppendInitializeTrace(diagLog);

					// CefSettings は Cef.Initialize() 内部で Dispose されるため毎回新規生成する
					AppendInitializeTrace("Calling Cef.Initialize");
					var initializeResult = CefSharp.Cef.Initialize(CreateCefSettings(browserSubprocessPath), performDependencyCheck: false, browserProcessHandler: null);
					AppendInitializeTrace($"Cef.Initialize returned: {initializeResult}, IsInitialized={CefSharp.Cef.IsInitialized}");

					if (initializeResult && (CefSharp.Cef.IsInitialized ?? false))
					{
						ClearInitializeFailureMarker();
						initialized = true;
						AppendInitializeTrace("Initialize completed successfully");
						return;
					}

					// 初期化失敗
					MarkInitializeFailure();
					var libcefExists = File.Exists(Path.Combine(cefDirectory, "libcef.dll"));
					throw new InvalidOperationException(
						$"Cef initialization failed. Result={initializeResult}, IsInitialized={CefSharp.Cef.IsInitialized}, " +
						$"AssemblyDirectory='{assemblyDirectory}', CefDirectory='{cefDirectory}', " +
						$"CachePath='{CachePath}', Is64BitProcess={Environment.Is64BitProcess}, " +
						$"libcef.Exists={libcefExists}('{Path.Combine(cefDirectory, "libcef.dll")}'), " +
						$"Subprocess.Exists={File.Exists(browserSubprocessPath)}('{browserSubprocessPath}'). " +
						$"See '{Path.Combine(CachePath, "cef-init-diag.log")}'");
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

		/// <summary>
		/// Cef.ExecuteProcess() より前に呼び出す必要があるネイティブ DLL パスを設定し、
		/// libcef.dll と chrome_elf.dll を明示的にロードします。
		/// SetDllDirectory の後に LoadLibrary で明示ロードすることで、
		/// C++/CLI アセンブリ (CefSharp.Core.Runtime.dll) がロードされる前に
		/// ネイティブ依存関係を確立します。
		/// </summary>
		public static void PrepareNativePaths()
		{
			Environment.CurrentDirectory = assemblyDirectory;
			SetDllDirectory(cefDirectory);

			// chrome_elf.dll → libcef.dll の順で明示ロード
			// これにより CefSharp.Core.Runtime.dll がロードされる際に
			// 依存関係が解決済みの状態になる
			var chromeElfPath = Path.Combine(cefDirectory, "chrome_elf.dll");
			var libCefPath = Path.Combine(cefDirectory, "libcef.dll");
			if (File.Exists(chromeElfPath))
				LoadLibrary(chromeElfPath);
			if (File.Exists(libCefPath))
				LoadLibrary(libCefPath);
		}

		/// <summary>
		/// CEF サブプロセスとして起動された場合に処理を委譲します。
		/// このメソッドは必ず PrepareNativePaths() の後に呼び出してください。
		/// NoInlining により、OnStartup の JIT コンパイル時に CefSharp がロードされるのを防ぎます。
		/// </summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static int ExecuteSubprocess()
		{
			return CefSharp.Cef.ExecuteProcess();
		}
	}
}
