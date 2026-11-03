using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Network;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Models.WebView2;
using Grabacr07.KanColleViewer.Services;
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
		private event PropertyChangedEventHandler? propertyChangedInternal;
		private Mutex? _appMutex;
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

			var appMutex = new Mutex(true, "KanColleViewer-{A3B4C5D6-E7F8-9012-ABCD-EF1234567890}", out var isFirstInstance);
			if (isFirstInstance)
			{
				AppNotificationService.Initialize();

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

				// MapCellNames.json も起動時だけ更新します。
				// MapCellNameProvider はゲーム中にローカルファイルのみを参照します。
				_ = MapCellNameUpdater.UpdateLocalFileAsync();

				// DaiNai_Ship.json も起動時だけ更新します。
				// DaiNaiShipProvider はゲーム中にローカルファイルのみを参照します。
				_ = DaiNaiShipProvider.UpdateLocalFileAsync();

				// TP_Ship_SlotItem.json も起動時だけ更新します。
				// TransportPointCalculator はゲーム中にローカルファイルのみを参照します。
				_ = TPSlotItemProvider.UpdateLocalFileAsync();

				GeneralSettings.Culture.Subscribe(x => ResourceService.Current.ChangeCulture(x)).AddTo(this);
				KanColleClient.Current.Settings = new KanColleSettings();

				AppThemeService.Current.Register(this, AppAccent.Purple, GeneralSettings.Theme.Value);
				GeneralSettings.Theme.Subscribe(x => AppThemeService.Current.ChangeTheme(x)).AddTo(this);

				Helper.SetMMCSSTask();

				var startedWithFallback = false;

				try
				{
					WebView2Setup.Prepare();
				}
				catch (Exception ex)
				{
					ReportRecoverableException("Startup.WebView2", this, ex);
					MessageBox.Show(
						$"{ex.Message}\r\n\r\nブラウザーを使わず、情報ウィンドウだけで起動します。上記の内容を確認・修正してから、アプリを再起動してください。\r\n（設定は情報ウィンドウの [設定] タブで変更できます）",
						ProductInfo.Title,
						MessageBoxButton.OK,
						MessageBoxImage.Warning);
					this.startedInFallbackMode = true;
					startedWithFallback = true;

					try
					{
						// フォールバック起動時はブラウザーに依存しない最小構成で起動する
						WindowService.Current.AddTo(this).Initialize(useInformationWindowAsMainWindow: true);

						PluginService.Current.AddTo(this).Initialize();
						NotifyService.Current.AddTo(this).Initialize();

						// 通常は KanColleWindowViewModel が行う設定画面の初期化 (保存済み設定の読み込み) を、ここで行う
						ViewModels.Settings.SettingsViewModel.Instance.Initialize();

						this.MainWindow = WindowService.Current.GetMainWindow();
						if (WindowService.Current.MainWindow is MainWindowViewModelBase fallbackMainWindowViewModel)
						{
							fallbackMainWindowViewModel.CanClose = true;
						}
						this.MainWindow.Show();
					}
					catch (Exception fallbackEx)
					{
						ReportException("StartupFallback", this, fallbackEx);
						MessageBox.Show(
							"WebView2 の起動準備に失敗し、情報ウィンドウだけでの起動にも失敗しました。アプリを終了します。",
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
				AppNotificationService.Shutdown();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Application: Notification shutdown error: {ex}");
			}

			try
			{
				System.Diagnostics.Debug.WriteLine("Application: Disposing resources...");
				this.compositeDisposable.Dispose();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Application: Resource disposal error: {ex}");
			}

			RelayHttpProxyService.Stop();
			this.CompleteExit(e);
		}

		private void CompleteExit(ExitEventArgs e)
		{
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

		#region INotifyPropertyChanged members

		event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
		{
			add { this.propertyChangedInternal += value; }
			remove { this.propertyChangedInternal -= value; }
		}

		private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
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
