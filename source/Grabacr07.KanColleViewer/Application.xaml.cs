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
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Cef;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.ViewModels;
using Grabacr07.KanColleViewer.Views;
using Grabacr07.KanColleWrapper;
using Livet;
using MetroRadiance.UI;
using MetroTrilithon.Lifetime;

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
		private readonly LivetCompositeDisposable compositeDisposable = new LivetCompositeDisposable();
		private event PropertyChangedEventHandler propertyChangedInternal;

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

			// 開発中に多重起動検知ついてると起動できなくて鬱陶しいので
			// デバッグ時は外すんじゃもん
#if !DEBUG
			var appInstance = new MetroTrilithon.Desktop.ApplicationInstance().AddTo(this);
			if (appInstance.IsFirst)
#endif
			{
				this.DispatcherUnhandledException += (sender, args) =>
				{
					ReportException("Dispatcher", sender, args.Exception);
					args.Handled = true;
				};

				DispatcherHelper.UIDispatcher = this.Dispatcher;

				SettingsHost.Load();
				this.compositeDisposable.Add(SettingsHost.Save);

				GeneralSettings.Culture.Subscribe(x => ResourceService.Current.ChangeCulture(x)).AddTo(this);
				KanColleClient.Current.Settings = new KanColleSettings();

				ThemeService.Current.Register(this, Theme.Dark, Accent.Purple);
				PluginService.Current.AddTo(this).Initialize();
				WindowService.Current.AddTo(this).Initialize();
				NotifyService.Current.AddTo(this).Initialize();

				Helper.SetMMCSSTask();
				Helper.DeleteCacheIfRequested();

				CefBridge.Initialize();

				// BootstrapProxy() で Views.Settings.ProxyBootstrapper.Show() が呼ばれるより前に
				// Application.MainWindow を設定しておく。これ大事
				// 後に設定した場合、Views.Settings.ProxyBootstrapper が閉じると共にアプリも終了してしまう。
				this.MainWindow = WindowService.Current.GetMainWindow();

				if (BootstrapProxy())
				{
					this.compositeDisposable.Add(ProxyBootstrapper.Shutdown);
					this.MainWindow.Show();

					var navigator = (WindowService.Current.MainWindow as KanColleWindowViewModel)?.Navigator;
					if (navigator != null)
					{
						navigator.Source = KanColleViewer.Properties.Settings.Default.KanColleUrl;
						navigator.Navigate();

					}
#if !DEBUG
					appInstance.CommandLineArgsReceived += (sender, args) =>
					{
						// 多重起動を検知したら、メイン ウィンドウを最前面に出す
						this.Dispatcher.Invoke(() => WindowService.Current.MainWindow.Activate());
						this.ProcessCommandLineParameter(args.CommandLineArgs);
					};
#endif
					base.OnStartup(e);
					this.ChangeState(ApplicationState.Running);
				}
				else
				{
					this.ChangeState(ApplicationState.Terminate);
					this.Shutdown();
				}
			}
#if !DEBUG
			else
			{
				appInstance.SendCommandLineArgs(e.Args);
				this.ChangeState(ApplicationState.Terminate);
				this.Shutdown();
			}
#endif
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

			base.OnExit(e);

			// 強制的にプロセスを終了（最終手段）
			// 通常は必要ないが、バックグラウンドスレッドが残っている場合の保険
			try
			{
				System.Diagnostics.Debug.WriteLine("Application: Forcing process exit...");
				Environment.Exit(0);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Application: Force exit error: {ex}");
			}
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
