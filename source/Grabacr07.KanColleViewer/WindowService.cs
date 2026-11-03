using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.ViewModels;
using Grabacr07.KanColleViewer.Views;
using Grabacr07.KanColleViewer.Win32;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;

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
		private readonly HashSet<Window> adjustedTopmostWindows = new HashSet<Window>();
		private static readonly DependencyPropertyDescriptor topmostDescriptor =
			DependencyPropertyDescriptor.FromProperty(Window.TopmostProperty, typeof(Window));
		private HwndSource? primaryWindowSource;
		private bool isWindowZOrderUpdatePending;
		private bool isUpdatingWindowZOrder;
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
				foreach (Window window in Application.Current.Windows)
				{
					var host = this.FindChild<Views.Controls.WebView2KanColleHost>(window);
					if (host is null) continue;
					host.ShowDevTools();
					return;
				}
				StatusService.Current.Notify("DevToolsを開けるWebView2が見つかりませんでした。");
			}
			catch (Exception ex)
			{
				StatusService.Current.Notify("DevTools の表示に失敗しました: " + ex.Message);
			}
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
			this.RequestWindowZOrderUpdate();
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
			this.ObserveWindowZOrder(window);

			if (GeneralSettings.FollowWindowsWithMainWindow.Value)
			{
				this.UpdateRelativeWindowLocation(window);
			}
			this.RequestWindowZOrderUpdate();
		}

		private void SetPrimaryWindow(Window window)
		{
			this.DetachPrimaryWindow();
			this.UntrackWindow(window);
			this.primaryWindow = window;
			window.LocationChanged += this.HandleMainWindowLocationChanged;
			window.Loaded += this.HandleMainWindowLoaded;
			window.Closed += this.HandleMainWindowClosed;
			window.SourceInitialized += this.HandleMainWindowSourceInitialized;
			this.ObserveWindowZOrder(window);
			this.AttachMainWindowSource();

			this.TrackOpenWindows();
			if (window.IsLoaded) this.RefreshRelativeWindowLocations();
			this.RequestWindowZOrderUpdate();
		}

		private void DetachPrimaryWindow()
		{
			this.primaryWindowSource?.RemoveHook(this.HandleMainWindowMessage);
			this.primaryWindowSource = null;
			if (this.primaryWindow == null) return;

			this.primaryWindow.LocationChanged -= this.HandleMainWindowLocationChanged;
			this.primaryWindow.Loaded -= this.HandleMainWindowLoaded;
			this.primaryWindow.Closed -= this.HandleMainWindowClosed;
			this.primaryWindow.SourceInitialized -= this.HandleMainWindowSourceInitialized;
			this.UnobserveWindowZOrder(this.primaryWindow);
			this.primaryWindow = null;
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
			if (sender is Window window)
			{
				this.adjustedTopmostWindows.Remove(window);
				this.UntrackWindow(window);
			}
			this.RequestWindowZOrderUpdate();
		}

		private void HandleMainWindowClosed(object? sender, EventArgs e)
		{
			if (!ReferenceEquals(sender, this.primaryWindow)) return;

			this.DetachPrimaryWindow();
			this.previousMainWindowLocation = null;
			this.relativeWindowLocations.Clear();
			this.RequestWindowZOrderUpdate();
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
			this.RequestWindowZOrderUpdate();
		}

		private void ObserveWindowZOrder(Window window)
		{
			window.Activated += this.HandleWindowZOrderChanged;
			window.StateChanged += this.HandleWindowZOrderChanged;
			window.IsVisibleChanged += this.HandleWindowVisibilityChanged;
			topmostDescriptor.AddValueChanged(window, this.HandleWindowZOrderChanged);
		}

		private void UnobserveWindowZOrder(Window window)
		{
			window.Activated -= this.HandleWindowZOrderChanged;
			window.StateChanged -= this.HandleWindowZOrderChanged;
			window.IsVisibleChanged -= this.HandleWindowVisibilityChanged;
			topmostDescriptor.RemoveValueChanged(window, this.HandleWindowZOrderChanged);
		}

		private void HandleWindowZOrderChanged(object? sender, EventArgs e) => this.RequestWindowZOrderUpdate();

		private void HandleWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => this.RequestWindowZOrderUpdate();

		private void HandleMainWindowSourceInitialized(object? sender, EventArgs e) => this.AttachMainWindowSource();

		private void AttachMainWindowSource()
		{
			if (this.primaryWindow == null || this.primaryWindowSource != null) return;

			var handle = new WindowInteropHelper(this.primaryWindow).Handle;
			if (handle == IntPtr.Zero) return;

			this.primaryWindowSource = HwndSource.FromHwnd(handle);
			this.primaryWindowSource?.AddHook(this.HandleMainWindowMessage);
		}

		private IntPtr HandleMainWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (message == NativeMethods.WM_WINDOWPOSCHANGED) this.RequestWindowZOrderUpdate();
			return IntPtr.Zero;
		}

		private void RequestWindowZOrderUpdate()
		{
			if (this.isDisposed || this.isUpdatingWindowZOrder || this.isWindowZOrderUpdatePending || Application.Current == null) return;

			this.isWindowZOrderUpdatePending = true;
			// 表示・アクティブ化に伴う Windows 側の重なり順変更が終わってから補正する。
			Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
			{
				this.isWindowZOrderUpdatePending = false;
				if (this.isDisposed) return;

				this.isUpdatingWindowZOrder = true;
				try
				{
					this.UpdateWindowZOrder();
				}
				finally
				{
					this.isUpdatingWindowZOrder = false;
				}
			}));
		}

		private void UpdateWindowZOrder()
		{
			var keepInFront = GeneralSettings.FollowWindowsWithMainWindow.Value
				&& this.primaryWindow is { Topmost: true, IsVisible: true, WindowState: not WindowState.Minimized };
			var windows = Application.Current.Windows.Cast<Window>()
				.Where(window => keepInFront && window.IsVisible && window.WindowState != WindowState.Minimized
					&& !ReferenceEquals(window, this.primaryWindow) && this.IsRelatedWindow(window))
				.ToHashSet();

			this.RestoreWindowTopmost(this.adjustedTopmostWindows.Except(windows).ToArray());
			if (windows.Count == 0) return;

			var handles = windows.ToDictionary(window => new WindowInteropHelper(window).Handle);
			var orderedWindows = new List<Window>();
			if (!NativeMethods.EnumWindows((handle, _) =>
			{
				if (handles.TryGetValue(handle, out var window)) orderedWindows.Add(window);
				return true;
			}, IntPtr.Zero))
			{
				NotifyWindowZOrderError();
				return;
			}

			// 既存のサブウィンドウ同士・所有ダイアログの重なり順を保ち、入力フォーカスは移さない。
			for (var index = orderedWindows.Count - 1; index >= 0; index--)
			{
				var window = orderedWindows[index];
				if (SetWindowTopmost(window, true)) this.adjustedTopmostWindows.Add(window);
			}
		}

		private bool IsRelatedWindow(Window window)
		{
			for (Window? current = window; current != null; current = current.Owner)
			{
				if (ReferenceEquals(current, this.primaryWindow) || this.followedWindows.Contains(current)) return true;
			}
			return false;
		}

		private static bool HasTopmostSetting(Window window)
		{
			for (Window? current = window; current != null; current = current.Owner)
			{
				if (current.Topmost) return true;
			}
			return false;
		}

		private void RestoreWindowTopmost(Window[] windows)
		{
			if (windows.Length == 0) return;

			var restoreWindows = windows.ToHashSet();
			if (Application.Current != null)
			{
				foreach (Window window in Application.Current.Windows)
				{
					for (var owner = window.Owner; owner != null; owner = owner.Owner)
					{
						if (!restoreWindows.Contains(owner)) continue;
						restoreWindows.Add(window);
						break;
					}
				}
			}

			// 最前面解除は非表示の所有ウィンドウにも及ぶため、ON のウィンドウは後で復元する。
			foreach (var window in restoreWindows.OrderBy(HasTopmostSetting))
			{
				if (new WindowInteropHelper(window).Handle == IntPtr.Zero || SetWindowTopmost(window, HasTopmostSetting(window)))
				{
					this.adjustedTopmostWindows.Remove(window);
				}
				else
				{
					this.adjustedTopmostWindows.Add(window);
				}
			}
		}

		private static bool SetWindowTopmost(Window window, bool topmost)
		{
			// WPF の Topmost は設定に双方向バインドされているので変更しない。
			var handle = new WindowInteropHelper(window).Handle;
			if (handle == IntPtr.Zero) return false;
			if (NativeMethods.SetWindowPos(handle, topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
				0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
				| NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER)) return true;

			NotifyWindowZOrderError();
			return false;
		}

		private static void NotifyWindowZOrderError()
		{
			var error = new Win32Exception(Marshal.GetLastWin32Error());
			System.Diagnostics.Trace.TraceError($"Window Z-order update failed: {error}");
			StatusService.Current.Notify("ウィンドウの重なり順を変更できませんでした: " + error.Message);
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
			this.UnobserveWindowZOrder(window);
			if (this.adjustedTopmostWindows.Contains(window)) this.RestoreWindowTopmost(new[] { window });
			this.relativeWindowLocations.Remove(window);
		}

		private static bool TryGetWindowLocation(Window window, out Point location)
		{
			location = new Point(window.Left, window.Top);
			return window.IsLoaded && double.IsFinite(location.X) && double.IsFinite(location.Y);
		}

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
			this.DetachPrimaryWindow();
			this.RestoreWindowTopmost(this.adjustedTopmostWindows.ToArray());
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
