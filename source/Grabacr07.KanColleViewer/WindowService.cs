using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.ViewModels;
using Grabacr07.KanColleViewer.Views;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using CefSharp.Wpf;
using CefSharp;

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

	public class WindowService : ObservableObject, IDisposableHolder
	{
		public static WindowService Current { get; } = new WindowService();

		private WindowServiceMode currentMode = (WindowServiceMode)(-1); // 初回で setter 入るように
		private InformationViewModel? information;
		private KanColleWindowViewModel? kanColleWindow;
		private InformationWindowViewModel? informationWindow;
		private Window? primaryWindow;
		private readonly HashSet<Window> followedWindows = new HashSet<Window>();
		private readonly Dictionary<Window, Point> relativeWindowLocations = new Dictionary<Window, Point>();
		private Point? previousMainWindowLocation;
		private bool isWindowTrackingInitialized;
		private bool isMovingFollowedWindows;
		private bool isDisposed;
		private readonly CompositeDisposable compositeDisposable = new CompositeDisposable();
		private readonly PropertyChangedEventHandler cultureChangedHandler;

		// 各艦隊の Situation 変化購読を管理する
		private CompositeDisposable fleetStateListeners = new CompositeDisposable();

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
					this.OnPropertyChanged(string.Empty);
				}
			}
		}

		/// <summary>
		/// 現在のメイン ウィンドウに提供されるデータを取得します。
		/// </summary>
		public MainWindowViewModelBase MainWindow { get; private set; } = null!;

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


		private WindowService()
		{
			this.cultureChangedHandler = (_, e) =>
			{
				if (e.PropertyName == nameof(ResourceService.Resources) && this.Mode == WindowServiceMode.Started)
				{
					StatusService.Current.Set(Resources.StatusBar_Ready);
				}
			};
			ResourceService.Current.PropertyChanged += this.cultureChangedHandler;
		}

		public void Initialize(bool useInformationWindowAsMainWindow = false)
		{
			this.InitializeWindowTracking();
			GeneralSettings.FollowWindowsWithMainWindow
				.Subscribe(this.HandleFollowWindowsSettingChanged)
				.AddTo(this);

			if (GeneralSettings.IsProxyMode || useInformationWindowAsMainWindow)
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
			this.fleetStateListeners = new CompositeDisposable();

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
			this.kanColleWindow?.RaiseZoomRequested();
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

		private ChromiumWebBrowser? FindFirstChromiumWebBrowser()
		{
			if (Application.Current == null) return null;

			foreach (Window w in Application.Current.Windows)
			{
				var found = FindChild<ChromiumWebBrowser>(w);
				if (found != null) return found;
			}

			return null;
		}

		private void InitializeWindowTracking()
		{
			if (!this.isWindowTrackingInitialized)
			{
				EventManager.RegisterClassHandler(
					typeof(Window),
					FrameworkElement.LoadedEvent,
					new RoutedEventHandler(this.HandleWindowLoaded),
					true);
				this.isWindowTrackingInitialized = true;
			}

			this.TrackOpenWindows();
		}

		private void HandleWindowLoaded(object sender, RoutedEventArgs e)
		{
			if (this.isDisposed || sender is not Window window) return;

			if (ReferenceEquals(window, this.primaryWindow))
			{
				this.RefreshRelativeWindowLocations();
			}
			else
			{
				this.TrackWindow(window);
			}
		}

		private void TrackOpenWindows()
		{
			if (Application.Current == null) return;

			foreach (Window window in Application.Current.Windows)
			{
				this.TrackWindow(window);
			}
		}

		private void TrackWindow(Window window)
		{
			if (this.isDisposed || ReferenceEquals(window, this.primaryWindow) || window.DataContext is DialogViewModel) return;
			if (!this.followedWindows.Add(window)) return;

			window.LocationChanged += this.HandleFollowedWindowLocationChanged;
			window.Closed += this.HandleFollowedWindowClosed;

			if (GeneralSettings.FollowWindowsWithMainWindow.Value)
			{
				this.UpdateRelativeWindowLocation(window);
			}
		}

		private void SetPrimaryWindow(Window window)
		{
			if (this.primaryWindow != null)
			{
				this.primaryWindow.LocationChanged -= this.HandleMainWindowLocationChanged;
				this.primaryWindow.Loaded -= this.HandleMainWindowLoaded;
				this.primaryWindow.Closed -= this.HandleMainWindowClosed;
			}

			this.UntrackWindow(window);
			this.primaryWindow = window;
			window.LocationChanged += this.HandleMainWindowLocationChanged;
			window.Loaded += this.HandleMainWindowLoaded;
			window.Closed += this.HandleMainWindowClosed;

			this.TrackOpenWindows();
			if (window.IsLoaded) this.RefreshRelativeWindowLocations();
		}

		private void HandleMainWindowLoaded(object sender, RoutedEventArgs e)
		{
			this.RefreshRelativeWindowLocations();
		}

		private void HandleMainWindowLocationChanged(object? sender, EventArgs e)
		{
			if (this.primaryWindow == null || !TryGetWindowLocation(this.primaryWindow, out var currentLocation))
			{
				this.previousMainWindowLocation = null;
				this.relativeWindowLocations.Clear();
				return;
			}

			var previousLocation = this.previousMainWindowLocation;
			this.previousMainWindowLocation = currentLocation;
			if (!GeneralSettings.FollowWindowsWithMainWindow.Value) return;

			if (previousLocation == null)
			{
				this.RefreshRelativeWindowLocations();
				return;
			}

			if (previousLocation.Value == currentLocation) return;

			this.isMovingFollowedWindows = true;
			try
			{
				foreach (var entry in this.relativeWindowLocations.ToArray())
				{
					var window = entry.Key;
					if (!window.IsLoaded || !TryGetWindowLocation(window, out _))
					{
						this.relativeWindowLocations.Remove(window);
						continue;
					}

					var left = currentLocation.X + entry.Value.X;
					var top = currentLocation.Y + entry.Value.Y;
					if (!double.IsFinite(left) || !double.IsFinite(top)) continue;

					if (window.Left != left) window.Left = left;
					if (window.IsLoaded && window.Top != top) window.Top = top;
				}
			}
			finally
			{
				this.isMovingFollowedWindows = false;
			}
		}

		private void HandleFollowedWindowLocationChanged(object? sender, EventArgs e)
		{
			if (this.isMovingFollowedWindows || !GeneralSettings.FollowWindowsWithMainWindow.Value || sender is not Window window) return;
			this.UpdateRelativeWindowLocation(window);
		}

		private void HandleFollowedWindowClosed(object? sender, EventArgs e)
		{
			if (sender is Window window) this.UntrackWindow(window);
		}

		private void HandleMainWindowClosed(object? sender, EventArgs e)
		{
			if (sender is not Window window) return;

			window.LocationChanged -= this.HandleMainWindowLocationChanged;
			window.Loaded -= this.HandleMainWindowLoaded;
			window.Closed -= this.HandleMainWindowClosed;
			if (!ReferenceEquals(window, this.primaryWindow)) return;

			this.primaryWindow = null;
			this.previousMainWindowLocation = null;
			this.relativeWindowLocations.Clear();
		}

		private void HandleFollowWindowsSettingChanged(bool followWindows)
		{
			if (followWindows)
			{
				this.TrackOpenWindows();
				this.RefreshRelativeWindowLocations();
			}
			else
			{
				this.relativeWindowLocations.Clear();
			}
		}

		private void RefreshRelativeWindowLocations()
		{
			this.relativeWindowLocations.Clear();
			if (this.primaryWindow == null || !TryGetWindowLocation(this.primaryWindow, out var mainLocation))
			{
				this.previousMainWindowLocation = null;
				return;
			}

			this.previousMainWindowLocation = mainLocation;
			if (!GeneralSettings.FollowWindowsWithMainWindow.Value) return;

			foreach (var window in this.followedWindows)
			{
				this.UpdateRelativeWindowLocation(window, mainLocation);
			}
		}

		private void UpdateRelativeWindowLocation(Window window)
		{
			if (this.primaryWindow == null || !TryGetWindowLocation(this.primaryWindow, out var mainLocation)) return;
			this.UpdateRelativeWindowLocation(window, mainLocation);
		}

		private void UpdateRelativeWindowLocation(Window window, Point mainLocation)
		{
			if (TryGetWindowLocation(window, out var windowLocation))
			{
				this.relativeWindowLocations[window] = new Point(
					windowLocation.X - mainLocation.X,
					windowLocation.Y - mainLocation.Y);
			}
		}

		private void UntrackWindow(Window window)
		{
			if (!this.followedWindows.Remove(window)) return;

			window.LocationChanged -= this.HandleFollowedWindowLocationChanged;
			window.Closed -= this.HandleFollowedWindowClosed;
			this.relativeWindowLocations.Remove(window);
		}

		private static bool TryGetWindowLocation(Window window, out Point location)
		{
			location = new Point(window.Left, window.Top);
			return window.IsLoaded && double.IsFinite(location.X) && double.IsFinite(location.Y);
		}

		/// <summary>
		/// アプリケーション内の最初の <see cref="ChromiumWebBrowser"/> を返します。
		/// </summary>
		public ChromiumWebBrowser? FindBrowser() => this.FindFirstChromiumWebBrowser();

		private T? FindChild<T>(DependencyObject parent) where T : DependencyObject
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
			var window = System.Windows.Application.Current?.MainWindow;
			window?.Left = 0.0;
		}


		public Window GetMainWindow()
		{
			if (this.MainWindow == this.kanColleWindow)
			{
				var window = new KanColleWindow { DataContext = this.kanColleWindow, };
				this.SetPrimaryWindow(window);
				return window;
			}
			if (this.MainWindow == this.informationWindow)
			{
				var window = new InformationWindow { DataContext = this.informationWindow, };
				this.SetPrimaryWindow(window);
				return window;
			}

			throw new InvalidOperationException();
		}


		#region disposable members

		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositeDisposable;

		public void Dispose()
		{
			this.isDisposed = true;
			if (this.primaryWindow != null)
			{
				this.primaryWindow.LocationChanged -= this.HandleMainWindowLocationChanged;
				this.primaryWindow.Loaded -= this.HandleMainWindowLoaded;
				this.primaryWindow.Closed -= this.HandleMainWindowClosed;
				this.primaryWindow = null;
			}
			foreach (var window in this.followedWindows.ToArray()) this.UntrackWindow(window);
			this.relativeWindowLocations.Clear();
			this.previousMainWindowLocation = null;

			ResourceService.Current.PropertyChanged -= this.cultureChangedHandler;
			this.fleetStateListeners?.Dispose();
			this.compositeDisposable.Dispose();
		}

		#endregion
	}
}
