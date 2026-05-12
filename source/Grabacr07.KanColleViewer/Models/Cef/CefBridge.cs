using System;
using System.Collections.Generic;
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
					EnsureVCRuntimeAvailable();  // ← 追加

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

					// GPU アクセラレーション設定（設定値に基づいて切り替え）
					if (GeneralSettings.IsGpuDisabled)
					{
						cefSettings.CefCommandLineArgs["disable-gpu"] = "1";
						cefSettings.CefCommandLineArgs["disable-gpu-compositing"] = "1";
					}

					// 開発者向けオプション: リモートデバッグポートの開放
					// 設定が有効な場合のみポートを開放する（デフォルト: 無効）
					if (GeneralSettings.IsRemoteDebuggingEnabled)
					{
						cefSettings.CefCommandLineArgs["remote-debugging-port"] = "9222";
					}

					// proxy-server は空でなければ設定する（Network.xaml の設定を残すため）
					var proxyString = Settings.NetworkSettings.LocalProxySettingsString;
					if (!string.IsNullOrWhiteSpace(proxyString))
					{
						cefSettings.CefCommandLineArgs["proxy-server"] = proxyString;
					}

					// ログ設定: デバッグビルドのみ出力、リリースビルドでは無効
#if DEBUG
					cefSettings.LogSeverity = LogSeverity.Info;
					cefSettings.LogFile = Path.Combine(CachePath, "cef.log");
#else
					cefSettings.LogSeverity = LogSeverity.Disable;
#endif

					CefSharpSettings.SubprocessExitIfParentProcessClosed = true;
					CefSharp.Cef.Initialize(cefSettings);

					// 初期化完了を確認
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

		public static Assembly ResolveCefSharpAssembly(object sender, ResolveEventArgs args)
		{
			if (!args.Name.StartsWith("CefSharp"))
				return null;

			// アセンブリ名を取得（"CefSharp.Wpf, Version=..." → "CefSharp.Wpf"）
			var shortName = args.Name.Split(new[] { ',' }, 2).FirstOrDefault();

			// ① パス区切り文字・ファイル名として不正な文字を含む名前は拒否
			if (string.IsNullOrEmpty(shortName)
				|| shortName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
				|| shortName.Contains('/') || shortName.Contains('\\'))
			{
				return null;
			}

			var assemblyFileName = shortName + ".dll";

			// ② 候補パスを列挙（cefDirectory 優先、assemblyDirectory にフォールバック）
			var candidates = new[]
			{
				Path.Combine(cefDirectory, assemblyFileName),
				Path.Combine(assemblyDirectory, assemblyFileName),
			};

			// 許可ディレクトリ（正規化済み）
			var allowedDirs = new[]
			{
				Path.GetFullPath(cefDirectory),
				Path.GetFullPath(assemblyDirectory),
			};

			foreach (var candidate in candidates)
			{
				// ③ Path.GetFullPath で正規化（"../" 等を解決）
				string fullPath;
				try
				{
					fullPath = Path.GetFullPath(candidate);
				}
				catch
				{
					// 不正なパス文字列は無視
					continue;
				}

				// ④ 正規化後のパスが許可ディレクトリ配下にあるか検証
				var isAllowed = allowedDirs.Any(dir =>
					fullPath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(fullPath, dir, StringComparison.OrdinalIgnoreCase));

				if (!isAllowed) continue;
				if (!File.Exists(fullPath)) continue;

				return Assembly.LoadFrom(fullPath);
			}

			return null;
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
