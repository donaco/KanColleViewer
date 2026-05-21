using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.ViewModels;
using Grabacr07.KanColleViewer.ViewModels.Messages;
using Grabacr07.KanColleViewer.Views;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Livet;
using Livet.Messaging;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;
using CefSharp.Wpf;
using CefSharp;
using StatefulModel;

namespace Grabacr07.KanColleViewer
{
	public enum WindowServiceMode
	{
		/// <summary>
		/// 艦これが起動されていません。
		/// </summary>
		NotStarted,

		/// <summary>
		/// 艦これが起動されています。
		/// </summary>
		Started,

		/// <summary>
		/// 艦これが起動されており、艦隊が出撃中です。
		/// </summary>
		InSortie,
	}

	public class WindowService : NotificationObject, IDisposableHolder
	{
		public static WindowService Current { get; } = new WindowService();

		private WindowServiceMode currentMode = (WindowServiceMode)(-1); // 初回で setter 入るように
		private InformationViewModel information;
		private KanColleWindowViewModel kanColleWindow;
		private InformationWindowViewModel informationWindow;
		private readonly LivetCompositeDisposable compositeDisposable = new LivetCompositeDisposable();

		// 各艦隊の Situation 変化購読を管理する
		private MultipleDisposable fleetStateListeners = new MultipleDisposable();

		// 大破時のアクセントカラー (Red)
		private static readonly Color HeavilyDamagedColor = Colors.Red;

		public WindowServiceMode Mode
		{
			get { return this.currentMode; }
			set
			{
				if (this.currentMode != value)
				{
					this.currentMode = value;
					switch (value)
					{
						case WindowServiceMode.NotStarted:
							var startContent = new StartContentViewModel(this.kanColleWindow?.Navigator);
							this.MainWindow.Content = startContent;
							this.MainWindow.StatusBar = startContent;
							StatusService.Current.Set(Resources.StatusBar_NotStarted);
							break;
						case WindowServiceMode.Started:
							this.MainWindow.Content = this.Information;
							this.MainWindow.StatusBar = this.Information.SelectedItem;
							StatusService.Current.Set(Resources.StatusBar_Ready);
							break;
					}

					this.UpdateAccent();
					this.RaisePropertyChanged();
				}
			}
		}

		/// <summary>
		/// 現在のメイン ウィンドウに提供されるデータを取得します。
		/// </summary>
		public MainWindowViewModelBase MainWindow { get; private set; }

		public InformationViewModel Information
		{
			get
			{
				if (this.information == null)
				{
					this.information = new InformationViewModel().AddTo(this);
					this.information
						.Subscribe(nameof(InformationViewModel.SelectedItem), () => this.MainWindow.StatusBar = this.Information.SelectedItem)
						.AddTo(this);
				}
				return this.information;
			}
		}


		private WindowService() { }

		public void Initialize()
		{
			if (GeneralSettings.IsProxyMode)
			{
				// プロキシ モード (艦これのウィンドウを表示しないやつ)
				// KanColleWindow は作らず、InformationWindow を MainWindow として運用する
				this.informationWindow = new InformationWindowViewModel(true);
				this.MainWindow = this.informationWindow;
			}
			else
			{
				// 通常モード ((艦これ + 情報ウィンドウ) or その分割)
				this.kanColleWindow = new KanColleWindowViewModel(true);
				this.MainWindow = this.kanColleWindow;
			}

			KanColleClient.Current.Subscribe(nameof(KanColleClient.IsStarted), this.UpdateMode).AddTo(this);
			KanColleClient.Current.Subscribe(nameof(KanColleClient.IsInSortie), this.UpdateMode).AddTo(this);

			// Homeport はログイン後に生成されるため、Homeport プロパティの変化を監視してから購読する
			KanColleClient.Current
				.Subscribe(nameof(KanColleClient.Homeport), this.OnHomeportChanged)
				.AddTo(this);
		}

		/// <summary>
		/// Homeport が生成されたとき（ログイン後）に艦隊リストの監視を開始します。
		/// </summary>
		private void OnHomeportChanged()
		{
			var homeport = KanColleClient.Current.Homeport;
			if (homeport == null) return;

			// 艦隊リストの変化を監視し、各艦隊の大破状態変化を購読し直す
			homeport.Organization
				.Subscribe(nameof(Organization.Fleets), this.RefreshFleetStateListeners)
				.AddTo(this);

			// 購読開始直後に一度実行して初期状態を反映する
			this.RefreshFleetStateListeners();
		}

		/// <summary>
		/// 艦隊リストが更新されたとき、各艦隊の Situation 変化購読を張り直します。
		/// </summary>
		private void RefreshFleetStateListeners()
		{
			this.fleetStateListeners?.Dispose();
			this.fleetStateListeners = new MultipleDisposable();

			foreach (var fleet in KanColleClient.Current.Homeport.Organization.Fleets.Values)
			{
				fleet.State
					.Subscribe(nameof(FleetState.Situation), this.UpdateAccent)
					.AddTo(this.fleetStateListeners);
			}
		}

		/// <summary>
		/// 現在のモードと大破状態に応じてアクセントカラーを更新します。
		/// </summary>
		private void UpdateAccent()
		{
			switch (this.currentMode)
			{
				case WindowServiceMode.NotStarted:
					AppThemeService.Current.ChangeAccent(AppAccent.Purple);
					break;

				case WindowServiceMode.Started:
					AppThemeService.Current.ChangeAccent(AppAccent.Blue);
					break;

				case WindowServiceMode.InSortie:
					// 出撃中の艦隊に大破艦がいる場合は赤、それ以外はオレンジ
					var hasHeavilyDamaged = KanColleClient.Current.Homeport.Organization.Fleets.Values
						.Any(f => f.IsInSortie && f.State.Situation.HasFlag(FleetSituation.HeavilyDamaged));
					if (hasHeavilyDamaged)
						AppThemeService.Current.ChangeAccent(HeavilyDamagedColor);
					else
						AppThemeService.Current.ChangeAccent(AppAccent.Orange);
					break;
			}
		}

		private void UpdateMode()
		{
			this.Mode = KanColleClient.Current.IsStarted
				? KanColleClient.Current.IsInSortie ? WindowServiceMode.InSortie : WindowServiceMode.Started
				: WindowServiceMode.NotStarted;
		}

		public void ClearZoomFactor()
		{
			this.kanColleWindow?.Messenger.Raise(new InteractionMessage { MessageKey = "WebBrowser.Zoom" });
		}

		// 追加: DevTools を開くためのユーティリティ
		public void ShowDevTools()
		{
			try
			{
				// Application の開いているウィンドウを検索して ChromiumWebBrowser を探す
				var browser = this.FindFirstChromiumWebBrowser();
				if (browser == null)
				{
					// 見つからなければ通知だけ行う
					StatusService.Current.Notify("DevTools を開けるブラウザが見つかりませんでした。");
					return;
				}

				// UI スレッドで ShowDevTools を呼ぶ（フォールバックを含む）
				browser.Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						browser.ShowDevTools();
					}
					catch (Exception)
					{
						try
						{
							browser.GetBrowser()?.GetHost()?.ShowDevTools();
						}
						catch (Exception ex)
						{
							StatusService.Current.Notify("DevTools の表示に失敗しました: " + ex.Message);
						}
					}
				}));
			}
			catch (Exception ex)
			{
				StatusService.Current.Notify("DevTools の表示に失敗しました: " + ex.Message);
			}
		}

		private ChromiumWebBrowser FindFirstChromiumWebBrowser()
		{
			if (Application.Current == null) return null;

			foreach (Window w in Application.Current.Windows)
			{
				var found = FindChild<ChromiumWebBrowser>(w);
				if (found != null) return found;
			}

			return null;
		}

		private T FindChild<T>(DependencyObject parent) where T : DependencyObject
		{
			if (parent == null) return null;

			var count = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < count; i++)
			{
				var child = VisualTreeHelper.GetChild(parent, i);
				if (child is T typed) return typed;
				var result = FindChild<T>(child);
				if (result != null) return result;
			}

			return null;
		}

		public void SetLocationLeft()
		{
			this.kanColleWindow?.Messenger.Raise(new SetWindowLocationMessage { MessageKey = "Window.Location", Left = 0.0 });
		}


		public Window GetMainWindow()
		{
			if (this.MainWindow == this.kanColleWindow)
			{
				return new KanColleWindow { DataContext = this.kanColleWindow, };
			}
			if (this.MainWindow == this.informationWindow)
			{
				return new InformationWindow { DataContext = this.informationWindow, };
			}

			throw new InvalidOperationException();
		}


		#region disposable members

		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositeDisposable;

		public void Dispose()
		{
			this.fleetStateListeners?.Dispose();
			this.compositeDisposable.Dispose();
		}

		#endregion
	}
}
