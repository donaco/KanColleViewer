using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using CefSharp;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Cef;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.ViewModels;
using Grabacr07.KanColleViewer.Views;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Lifetime; // Phase 1: Infrastructure/Lifetime に内製化済み

namespace Grabacr07.KanColleViewer
{
	/// <summary>
	/// アプリケーションの状態を示す識別子を定義します。
	/// </summary>
	public enum ApplicationState
	{
		/// <summary>
		/// アプリケーションは起動中です。
		/// </summary>
		Startup,

		/// <summary>
		/// アプリケーションは起動準備が完了し、実行中です。
		/// </summary>
		Running,

		/// <summary>
		/// アプリケーションは終了したか、または終了処理中です。
		/// </summary>
		Terminate,
	}

	sealed partial class Application : INotifyPropertyChanged, IDisposableHolder
	{
		private readonly CompositeDisposable compositeDisposable = new CompositeDisposable();
		private event PropertyChangedEventHandler propertyChangedInternal;
		private Mutex _appMutex;
		private bool startedInFallbackMode;

		public DirectoryInfo LocalAppData = new DirectoryInfo(
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				ProductInfo.Company,
				ProductInfo.Product));

		/// <summary>
		/// アプリケーションの現在の状態を示す識別子を取得します。
		/// </summary>
		public ApplicationState State { get; private set; }


		protected override void OnStartup(StartupEventArgs e)
		{
			this.ChangeState(ApplicationState.Startup);

			var commandLineArgs = Environment.GetCommandLineArgs();

			// ★ SetDllDirectory を先に確立する（PrepareNativePaths は CefSharp 型を参照しないため安全）
			CefBridge.PrepareNativePaths();

			// ★ Cef.ExecuteProcess() は CefBridge.ExecuteSubprocess() 経由で呼ぶ
			// OnStartup メソッド内に直接 Cef 型の参照があると、このメソッドの JIT コンパイル時に
			// CefSharp.Core.Runtime.dll (C++/CLI) が SetDllDirectory 呼び出し前にロードされ、
			// libcef.dll が見つからないまま内部が null 状態で初期化されてしまう。
			var cefSubprocessExitCode = CefBridge.ExecuteSubprocess();
			if (cefSubprocessExitCode >= 0)
			{
				this.Shutdown(cefSubprocessExitCode);
				return;
			}

			var appMutex = new Mutex(true, "KanColleViewer-{A3B4C5D6-E7F8-9012-ABCD-EF1234567890}", out var isFirstInstance);
			if (isFirstInstance)
			{
				this.DispatcherUnhandledException += (sender, args) =>
				{
					if (args.Exception is DllNotFoundException dllEx)
					{
						MessageBox.Show(dllEx.Message, ProductInfo.Title, MessageBoxButton.OK, MessageBoxImage.Error);
						args.Handled = true;
						this.Shutdown();
						return;
					}

					if (this.State == ApplicationState.Startup || args.Exception is System.Windows.Markup.XamlParseException)
					{
						ReportException("Dispatcher", sender, args.Exception);
						args.Handled = true;
						return;
					}

					ReportRecoverableException("Dispatcher", sender, args.Exception);
					args.Handled = true;  // 例外を処理済みとしてアプリ続行
				};

				SettingsHost.Load();
				this.compositeDisposable.Add(new DelegateDisposable(SettingsHost.Save));

				// EventMap.json は起動時だけ更新します。
				// ゲーム中の SallyArea.GetAsync() はローカルファイルのみを参照します。
				_ = SallyArea.UpdateLocalFileAsync();

				GeneralSettings.Culture.Subscribe(x => ResourceService.Current.ChangeCulture(x)).AddTo(this);
				KanColleClient.Current.Settings = new KanColleSettings();

				AppThemeService.Current.Register(this, AppAccent.Purple);

							Helper.SetMMCSSTask();
							Helper.DeleteCacheIfRequested();

							var startedWithFallback = false;

							try
							{
								CefBridge.Initialize();
							}
							catch (Exception ex)
							{

								ReportRecoverableException("Startup.CefInitialize", this, ex);
								this.startedInFallbackMode = true;
								startedWithFallback = true;

								try
								{
									// フォールバック起動時はブラウザーに依存しない最小構成で起動する
									WindowService.Current.AddTo(this).Initialize(useInformationWindowAsMainWindow: true);

									PluginService.Current.AddTo(this).Initialize();
									NotifyService.Current.AddTo(this).Initialize();

									this.MainWindow = WindowService.Current.GetMainWindow();
									if (WindowService.Current.MainWindow is MainWindowViewModelBase fallbackMainWindowViewModel)
									{
										fallbackMainWindowViewModel.CanClose = true;
									}
									this.MainWindow.Closed += (s, args) =>
									{
										try
										{
											Environment.Exit(0);
										}
										catch
										{
										}
									};
									this.MainWindow.Show();
								}
								catch (Exception fallbackEx)
								{
									ReportException("StartupFallback", this, fallbackEx);
									MessageBox.Show(
										"起動中にブラウザーエンジン (Cef) の初期化に失敗し、代替モードでの起動にも失敗しました。cef.log を確認してください。",
										ProductInfo.Title,
										MessageBoxButton.OK,
										MessageBoxImage.Error);
									this.Shutdown();
									return;
								}
							}

							if (!startedWithFallback)
							{
								try
								{
									WindowService.Current.AddTo(this).Initialize();
									this.MainWindow = WindowService.Current.GetMainWindow();
									this.MainWindow.Show();

									var navigator = (WindowService.Current.MainWindow as KanColleWindowViewModel)?.Navigator;
									if (navigator != null)
									{
										navigator.Source = KanColleViewer.Properties.Settings.Default.KanColleUrl;
										navigator.Navigate();
									}
								}
								catch (Exception windowEx)
								{
									ReportException("Startup.Window", this, windowEx);
									MessageBox.Show(
										"メインウィンドウの初期化でエラーが発生しました。ErrorReports を確認してください。",
										ProductInfo.Title,
										MessageBoxButton.OK,
										MessageBoxImage.Error);
									this.Shutdown();
									return;
								}

								try
								{
									PluginService.Current.AddTo(this).Initialize();
									NotifyService.Current.AddTo(this).Initialize();
								}
								catch (Exception pluginEx)
								{
									ReportRecoverableException("Startup.PluginOrNotify", this, pluginEx);
								}
							}

				// appMutex はアプリ終了まで保持（GC 対策でフィールドに保存）
				_appMutex = appMutex;
				base.OnStartup(e);
				// フォールバックモード時は Running にしない（CanClose が常に true になるよう State を Startup のままにする）
				if (!this.startedInFallbackMode)
				{
					this.ChangeState(ApplicationState.Running);
				}
			}
			else
			{
				appMutex.Dispose();
				this.ChangeState(ApplicationState.Terminate);
				this.Shutdown();
			}
		}


		protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
		{
			var confirmation = GeneralSettings.ExitConfirmationType == ExitConfirmationType.Always
							   || (GeneralSettings.ExitConfirmationType == ExitConfirmationType.InSortieOnly && KanColleClient.Current.IsInSortie);
			if (confirmation)
			{
				var vmodel = new DialogViewModel();
				var window = new ExitDialog
				{
					DataContext = vmodel,
					Owner = this.MainWindow,
				};
				window.ShowDialog();

				e.Cancel = !vmodel.DialogResult;
			}

			base.OnSessionEnding(e);
		}
		#region アプリ終了処理
		protected override void OnExit(ExitEventArgs e)
		{
			this.ChangeState(ApplicationState.Terminate);

			try
			{
				// CefSharp の完全シャットダウンを待つ
				System.Diagnostics.Debug.WriteLine("Application: Shutting down CefSharp...");
				Cef.Shutdown();

				// CefSharp のシャットダウン完了を待機（最大5秒）
				var stopwatch = System.Diagnostics.Stopwatch.StartNew();
				while (Cef.IsInitialized == true && stopwatch.ElapsedMilliseconds < 5000)
				{
					Thread.Sleep(100);
				}
				System.Diagnostics.Debug.WriteLine($"Application: CefSharp shutdown completed in {stopwatch.ElapsedMilliseconds}ms");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Application: CefSharp shutdown error: {ex}");
			}

					try
						{
							// Dispose を先に実行してリソース解放
							System.Diagnostics.Debug.WriteLine("Application: Disposing resources...");
							this.compositeDisposable.Dispose();
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"Application: Resource disposal error: {ex}");
						}

			try
			{
				this._appMutex?.ReleaseMutex();
				this._appMutex?.Dispose();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Application: Mutex dispose error: {ex}");
			}

			base.OnExit(e);

			// 強制的にプロセスを終了（最終手段）
			// 通常は必要ないが、バックグラウンドスレッドが残っている場合の保険
#if !DEBUG
			try
			{
				System.Diagnostics.Debug.WriteLine("Application: Forcing process exit...");
				Environment.Exit(0);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Application: Force exit error: {ex}");
			}
#else
			if (this.startedInFallbackMode)
			{
				try
				{
					System.Diagnostics.Debug.WriteLine("Application: Forcing process exit in fallback mode...");
					Environment.Exit(0);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Application: Force exit error: {ex}");
				}
			}
#endif
		}
		#endregion

		/// <summary>
		/// <see cref="State"/> プロパティを更新し、<see cref="INotifyPropertyChanged.PropertyChanged"/> イベントを発生させます。
		/// </summary>
		/// <param name="value"></param>
		private void ChangeState(ApplicationState value)
		{
			if (this.State == value) return;

			this.State = value;
			this.RaisePropertyChanged(nameof(this.State));
		}

		private void ProcessCommandLineParameter(string[] args)
		{
			Debug.WriteLine("多重起動検知: " + args.ToString(" "));

			// コマンド ライン引数付きで多重起動されたときに何かできる
			// けど今やることがない
		}


		#region INotifyPropertyChanged members

		event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
		{
			add { this.propertyChangedInternal += value; }
			remove { this.propertyChangedInternal -= value; }
		}

		private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
		{
			this.propertyChangedInternal?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion

		#region IDisposable members

		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositeDisposable;

		void IDisposable.Dispose()
		{
			this.compositeDisposable.Dispose();
		}

		#endregion
	}
}
