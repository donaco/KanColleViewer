using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Models.WebView2;
using MetroTrilithon.Mvvm;
using Microsoft.Web.WebView2.Core;
using WebView2Browser = Microsoft.Web.WebView2.Wpf.WebView2;

namespace Grabacr07.KanColleViewer.Views.Controls
{
	public sealed class WebView2KanColleHost : ContentControl, IDisposable
	{
		private readonly WebView2Browser browser = new WebView2Browser();
		private readonly TextBlock message = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black };
		private readonly TextBlock runtimeVersion = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.Black,
			Background = Brushes.LightGray,
			Padding = new Thickness(6),
			FontWeight = FontWeights.SemiBold,
		};
		private readonly StackPanel header = new StackPanel();
		private Task? initialization;
		private IDisposable? muteSubscription;
		private Window? owner;
		private Uri? pendingUri;
		private bool ready;
		private bool disposed;

		public INavigator? Navigator
		{
			get => (INavigator?)this.GetValue(NavigatorProperty);
			set => this.SetValue(NavigatorProperty, value);
		}

		public static readonly DependencyProperty NavigatorProperty = DependencyProperty.Register(
			nameof(Navigator), typeof(INavigator), typeof(WebView2KanColleHost),
			new PropertyMetadata(null, (d, e) => ((WebView2KanColleHost)d).ChangeNavigator((INavigator?)e.OldValue, (INavigator?)e.NewValue)));

		public WindowViewModel? ViewModel
		{
			get => (WindowViewModel?)this.GetValue(ViewModelProperty);
			set => this.SetValue(ViewModelProperty, value);
		}

		public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
			nameof(ViewModel), typeof(WindowViewModel), typeof(WebView2KanColleHost),
			new PropertyMetadata(null, (d, e) => ((WebView2KanColleHost)d).ChangeViewModel((WindowViewModel?)e.OldValue, (WindowViewModel?)e.NewValue)));

		public double ZoomFactor
		{
			get => (double)this.GetValue(ZoomFactorProperty);
			set => this.SetValue(ZoomFactorProperty, value);
		}

		public static readonly DependencyProperty ZoomFactorProperty = DependencyProperty.Register(
			nameof(ZoomFactor), typeof(double), typeof(WebView2KanColleHost),
			new PropertyMetadata(1.0, (d, _) => ((WebView2KanColleHost)d).ApplySize()));

		public event EventHandler<Size>? OwnerSizeChangeRequested;

		public WebView2KanColleHost()
		{
			var panel = new DockPanel();
			var warning = new TextBlock
			{
				Text = "WebView2移行検証（段階2）: API解析・情報更新・出撃状態判定は未対応です。\n撮影・ゲーム専用CSSは段階4で対応します。通常利用・出撃にはCEF版をご利用ください。",
				TextWrapping = TextWrapping.Wrap,
				Foreground = Brushes.Black,
				Background = Brushes.LightGoldenrodYellow,
				Padding = new Thickness(6),
			};
			this.header.Children.Add(warning);
			this.header.Children.Add(this.runtimeVersion);
			this.header.Children.Add(this.message);
			DockPanel.SetDock(this.header, Dock.Top);
			panel.Children.Add(this.header);
			panel.Children.Add(this.browser);
			this.Content = panel;
			this.Loaded += this.HandleLoaded;
		}

		private async void HandleLoaded(object sender, RoutedEventArgs e)
		{
			if (this.disposed) return;
			if (this.owner is null)
			{
				this.owner = Window.GetWindow(this);
				if (this.owner is not null) this.owner.Closed += this.HandleOwnerClosed;
				this.muteSubscription = GeneralSettings.IsMuted.Subscribe(_ => this.ApplyMute());
			}
			this.ApplySize();
			await this.InitializeSafelyAsync();
		}

		private async Task InitializeSafelyAsync()
		{
			if (this.disposed) return;
			try
			{
				await (this.initialization ??= this.InitializeAsync());
			}
			catch (Exception ex)
			{
				if (this.disposed)
				{
					Debug.WriteLine($"WebView2 initialization interrupted during shutdown: {ex.GetType().Name}");
					return;
				}
				this.ready = false;
				if (this.Navigator is not null) this.Navigator.IsNavigating = false;
				this.ReportError($"WebView2の初期化に失敗しました: {ex.GetType().Name}: {ex.Message}。アプリを再起動してください。");
			}
		}

		private async Task InitializeAsync()
		{
			this.message.Text = "WebView2を初期化しています。";
			var environment = await WebView2Migration.GetEnvironmentAsync();
			if (this.disposed) return;
			await this.browser.EnsureCoreWebView2Async(environment);
			if (this.disposed) return;
			this.runtimeVersion.Text = string.IsNullOrWhiteSpace(WebView2Migration.RuntimeVersion)
				? "WebView2 Runtimeのバージョンを取得できませんでした。"
				: $"WebView2 Runtime バージョン: {WebView2Migration.RuntimeVersion}";
			var core = this.browser.CoreWebView2;
			core.Settings.AreDevToolsEnabled = WebView2Migration.IsDevToolsEnabled;
			core.Settings.AreDefaultContextMenusEnabled = false;
			core.NavigationStarting += this.HandleNavigationStarting;
			core.NavigationCompleted += this.HandleNavigationCompleted;
			core.HistoryChanged += this.HandleHistoryChanged;
			core.SourceChanged += this.HandleSourceChanged;
			core.ProcessFailed += this.HandleProcessFailed;
			core.DownloadStarting += this.HandleDownloadStarting;
			core.NewWindowRequested += this.HandleNewWindowRequested;
			if (WebView2Migration.ClearCacheOnStartup)
			{
				await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
				if (this.disposed) return;
				GeneralSettings.ClearCacheOnNextStartup.Value = false;
			}
			core.IsMuted = GeneralSettings.IsMuted.Value;
			this.ready = true;
			this.message.Text = "初期化が完了しました。30 / 60 FPS固定は未対応です。";
			this.ApplySize();
			if (this.pendingUri is not null)
			{
				var uri = this.pendingUri;
				this.pendingUri = null;
				core.Navigate(uri.AbsoluteUri);
			}
		}

		private void ChangeNavigator(INavigator? oldValue, INavigator? newValue)
		{
			if (oldValue is not null)
			{
				oldValue.UriRequested -= this.HandleUriRequested;
				oldValue.GoBackRequested -= this.HandleGoBack;
				oldValue.GoForwardRequested -= this.HandleGoForward;
				oldValue.RefreshRequested -= this.HandleRefresh;
			}
			if (newValue is not null && !this.disposed)
			{
				newValue.UriRequested += this.HandleUriRequested;
				newValue.GoBackRequested += this.HandleGoBack;
				newValue.GoForwardRequested += this.HandleGoForward;
				newValue.RefreshRequested += this.HandleRefresh;
				this.UpdateNavigator();
			}
		}

		private void ChangeViewModel(WindowViewModel? oldValue, WindowViewModel? newValue)
		{
			if (oldValue is not null)
			{
				oldValue.ZoomRequested -= this.HandleZoomRequested;
				oldValue.ScreenshotRequested -= this.HandleScreenshotRequested;
			}
			if (newValue is not null && !this.disposed)
			{
				newValue.ZoomRequested += this.HandleZoomRequested;
				newValue.ScreenshotRequested += this.HandleScreenshotRequested;
			}
		}

		private void HandleUriRequested(object? sender, Uri uri)
		{
			if (this.disposed) return;
			if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				this.ReportError("httpまたはhttps以外には移動できません。");
				return;
			}
			if (!this.ready)
			{
				this.pendingUri = uri;
				if (this.initialization?.IsCompleted == true)
					this.ReportError("WebView2は利用できません。アプリを再起動してください。");
				return;
			}
			this.RunBrowserAction(() => this.browser.CoreWebView2.Navigate(uri.AbsoluteUri));
		}

		private void HandleNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
		{
			if (e.Uri.IndexOf("kcsapi", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				e.Cancel = true;
				this.ReportError("ゲームAPIへのメイン画面の移動を拒否しました。");
				return;
			}
			if (this.Navigator is not null) this.Navigator.IsNavigating = true;
		}

		private void HandleNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
		{
			if (this.Navigator is not null) this.Navigator.IsNavigating = false;
			this.UpdateNavigator();
			if (!e.IsSuccess) this.ReportError($"ページの読み込みに失敗しました: {e.WebErrorStatus}");
		}

		private void HandleHistoryChanged(object? sender, object e) => this.UpdateNavigator();
		private void HandleSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e) => this.UpdateNavigator();

		private void UpdateNavigator()
		{
			if (this.Navigator is null || !this.ready || this.disposed) return;
			var core = this.browser.CoreWebView2;
			if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)) this.Navigator.Source = uri;
			this.Navigator.CanGoBack = core.CanGoBack;
			this.Navigator.CanGoForward = core.CanGoForward;
		}

		private void HandleGoBack(object? sender, EventArgs e) => this.RunBrowserAction(() =>
		{
			if (this.browser.CoreWebView2.CanGoBack) this.browser.CoreWebView2.GoBack();
		});

		private void HandleGoForward(object? sender, EventArgs e) => this.RunBrowserAction(() =>
		{
			if (this.browser.CoreWebView2.CanGoForward) this.browser.CoreWebView2.GoForward();
		});

		private void HandleRefresh(object? sender, EventArgs e) => this.RunBrowserAction(() => this.browser.CoreWebView2.Reload());
		private void HandleZoomRequested(object? sender, EventArgs e) => this.ApplySize();
		private void HandleScreenshotRequested(object? sender, ScreenshotRequestedEventArgs e) => this.ReportError("WebView2検証起動の撮影は段階4まで未対応です。比較試作の撮影機能をご利用ください。");

		private void HandleDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
		{
			e.Cancel = true;
			this.ReportError("ダウンロードを拒否しました。");
		}

		private void HandleNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
		{
			e.Handled = true;
			this.ReportError("新しいブラウザーウィンドウを開く要求は検証起動では未対応です。");
		}

		private void HandleProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
		{
			this.ready = false;
			if (this.Navigator is not null)
			{
				this.Navigator.IsNavigating = false;
				this.Navigator.CanGoBack = false;
				this.Navigator.CanGoForward = false;
			}
			this.ReportError($"WebView2のプロセス障害: {e.ProcessFailedKind}。アプリを再起動してください。");
		}

		public void ShowDevTools() => this.RunBrowserAction(() =>
		{
			if (!this.browser.CoreWebView2.Settings.AreDevToolsEnabled)
				throw new InvalidOperationException("開発者向けオプションを有効にして再起動してください。");
			this.browser.CoreWebView2.OpenDevToolsWindow();
		});

		private void ApplyMute()
		{
			if (this.ready && !this.disposed)
				this.RunBrowserAction(() => this.browser.CoreWebView2.IsMuted = GeneralSettings.IsMuted.Value);
		}

		internal void ApplySize()
		{
			if (this.disposed) return;
			if (!double.IsFinite(this.ZoomFactor) || this.ZoomFactor < 0.1 || this.ZoomFactor > 10)
			{
				this.ReportError("表示倍率は10%～1000%で指定してください。");
				return;
			}
			this.Width = 1200 * this.ZoomFactor;
			this.header.Measure(new Size(this.Width, double.PositiveInfinity));
			this.Height = 720 * this.ZoomFactor + this.header.DesiredSize.Height;
			if (this.ready) this.RunBrowserAction(() => this.browser.ZoomFactor = this.ZoomFactor);
			if (this.IsLoaded) this.OwnerSizeChangeRequested?.Invoke(this, new Size(this.Width, this.Height));
		}

		private void RunBrowserAction(Action action)
		{
			if (this.disposed) return;
			if (!this.ready)
			{
				this.ReportError("WebView2は準備中または利用できない状態です。");
				return;
			}
			try { action(); }
			catch (Exception ex) { this.ReportError($"ブラウザー操作に失敗しました: {ex.GetType().Name}: {ex.Message}"); }
		}

		private void ReportError(string text)
		{
			this.message.Text = text;
			StatusService.Current.Notify(text);
			Debug.WriteLine(text);
		}

		private void HandleOwnerClosed(object? sender, EventArgs e) => this.Dispose();

		public void Dispose()
		{
			if (this.disposed) return;
			this.disposed = true;
			this.ready = false;
			this.pendingUri = null;
			this.Loaded -= this.HandleLoaded;
			if (this.owner is not null) this.owner.Closed -= this.HandleOwnerClosed;
			this.ChangeNavigator(this.Navigator, null);
			this.ChangeViewModel(this.ViewModel, null);
			this.muteSubscription?.Dispose();
			this.browser.Dispose();
		}
	}
}
