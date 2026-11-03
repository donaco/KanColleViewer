using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Network;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Models.WebView2;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Mvvm;
using MetroTrilithon.UI.Interactivity;
using Microsoft.Web.WebView2.Core;
using Microsoft.Xaml.Behaviors;
using AppResources = Grabacr07.KanColleViewer.Properties.Resources;
using WebView2Browser = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;

namespace Grabacr07.KanColleViewer.Views.Controls
{
	public sealed class WebView2KanColleHost : ContentControl, IDisposable
	{
		private const string SuppressTabKeyScript = "window.addEventListener('keydown', e => { if (e.key === 'Tab') e.preventDefault(); }, true);";

		public static Size KanColleSize { get; } = new Size(1200.0, 720.0);

		private readonly WebView2Browser browser = new WebView2Browser();
		private readonly HashSet<CoreWebView2Frame> trackedFrames = new();
		private readonly HashSet<CoreWebView2Frame> loadedFrames = new();
		private readonly Dictionary<CoreWebView2Frame, string> frameSources = new();
		// アプリの再起動が必要な重大なエラーだけを、ゲーム画面の上に重ねて表示します。
		private readonly TextBlock message = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.Black,
			Background = Brushes.LightGoldenrodYellow,
			Padding = new Thickness(6),
			VerticalAlignment = VerticalAlignment.Top,
			Visibility = Visibility.Collapsed,
		};
		private Task? initialization;
		private IDisposable? muteSubscription;
		private Window? owner;
		private Uri? pendingUri;
		private CoreWebView2? core;
		private readonly CancellationTokenSource captureCancellation = new();
		private readonly object captureQueueLock = new();
		private Task captureQueue = Task.CompletedTask;
		private int captureFailureCount;
		private bool maintenanceNavigationStarted;
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

		public string UserStyleSheet
		{
			get => (string)this.GetValue(UserStyleSheetProperty);
			set => this.SetValue(UserStyleSheetProperty, value);
		}

		public static readonly DependencyProperty UserStyleSheetProperty = DependencyProperty.Register(
			nameof(UserStyleSheet), typeof(string), typeof(WebView2KanColleHost),
			new PropertyMetadata(string.Empty, (d, _) => ((WebView2KanColleHost)d).ApplyUserStyleSheetToCurrentDocuments()));

		public event EventHandler<Size>? OwnerSizeChangeRequested;

		public WebView2KanColleHost()
		{
			var panel = new Grid();
			var browserViewport = new BrowserViewport
			{
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Content = this.browser,
			};
			panel.Children.Add(browserViewport);
			panel.Children.Add(this.message);
			// タスクバーのプレビュー範囲をゲーム画面に合わせます。環境によってはウィンドウ全体が表示されます。
			Interaction.GetBehaviors(this.browser).Add(new TaskbarThumbnailBehavior());
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
				this.ReportFatalError($"WebView2の初期化に失敗しました: {ex.GetType().Name}: {ex.Message}。アプリを再起動してください。");
			}
		}

		private async Task InitializeAsync()
		{
			var environment = await WebView2Setup.GetEnvironmentAsync();
			if (this.disposed) return;
			await this.browser.EnsureCoreWebView2Async(environment);
			if (this.disposed) return;
			var core = this.browser.CoreWebView2;
			this.core = core;
			core.FrameCreated += this.HandleFrameCreated;
			core.Settings.AreDevToolsEnabled = WebView2Setup.IsDevToolsEnabled;
			core.Settings.AreDefaultContextMenusEnabled = false;
			core.NavigationStarting += this.HandleNavigationStarting;
			core.DOMContentLoaded += this.HandleDOMContentLoaded;
			core.NavigationCompleted += this.HandleNavigationCompleted;
			core.HistoryChanged += this.HandleHistoryChanged;
			core.SourceChanged += this.HandleSourceChanged;
			core.ProcessFailed += this.HandleProcessFailed;
			core.DownloadStarting += this.HandleDownloadStarting;
			core.NewWindowRequested += this.HandleNewWindowRequested;
			core.WebResourceResponseReceived += this.HandleWebResourceResponseReceived;
			core.ServerCertificateErrorDetected += this.HandleServerCertificateErrorDetected;
			// Tab は文字になるキーのため WPF のキーイベントへ転送されません。全フレームのページ側で既定動作を止めます。
			await core.AddScriptToExecuteOnDocumentCreatedAsync(SuppressTabKeyScript);
			if (this.disposed) return;
			if (WebView2Setup.ClearCacheOnStartup)
			{
				await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
				if (this.disposed) return;
				GeneralSettings.ClearCacheOnNextStartup.Value = false;
			}
			core.IsMuted = GeneralSettings.IsMuted.Value;
			this.ready = true;
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
			if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
				&& uri.AbsolutePath.IndexOf("kcsapi", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				e.Cancel = true;
				this.ReportError("ゲームAPIへのメイン画面の移動を拒否しました。");
				return;
			}
			this.ResetTrackedFrames();
			if (this.Navigator is not null) this.Navigator.IsNavigating = true;
		}

		private void HandleFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e) => this.TrackFrame(e.Frame);

		private void HandleNestedFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e) => this.TrackFrame(e.Frame);

		private void TrackFrame(CoreWebView2Frame frame)
		{
			if (this.disposed || !this.trackedFrames.Add(frame)) return;
			frame.FrameCreated += this.HandleNestedFrameCreated;
			frame.NavigationStarting += this.HandleFrameNavigationStarting;
			frame.DOMContentLoaded += this.HandleFrameDOMContentLoaded;
			frame.Destroyed += this.HandleFrameDestroyed;
		}

		private void HandleFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
		{
			if (IsBlockedFrameUri(e.Uri))
			{
				e.Cancel = true;
				return;
			}
			if (sender is not CoreWebView2Frame frame) return;
			this.loadedFrames.Remove(frame);
			this.frameSources[frame] = e.Uri;
		}

		private static bool IsBlockedFrameUri(string address)
		{
			if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
			var host = uri.Host;
			return IsHostOrSubdomain(host, "twitter.com") || IsHostOrSubdomain(host, "adsrvr.org");
		}

		private static bool IsHostOrSubdomain(string host, string domain)
		{
			return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
				|| host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
		}

		private async void HandleFrameDOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
		{
			if (sender is not CoreWebView2Frame frame || this.disposed || !this.trackedFrames.Contains(frame) || frame.IsDestroyed() != 0) return;
			this.loadedFrames.Add(frame);
			try
			{
				if (!this.frameSources.TryGetValue(frame, out var source))
				{
					var sourceResult = await frame.ExecuteScriptAsync("location.href");
					if (this.disposed || !this.trackedFrames.Contains(frame) || frame.IsDestroyed() != 0) return;
					source = JsonSerializer.Deserialize<string>(sourceResult) ?? string.Empty;
					this.frameSources[frame] = source;
				}
				if (this.ready && WebView2HttpCapture.IsGamePageUrl(source)) await this.ApplyUserStyleSheetToFrameAsync(frame);
			}
			catch (Exception ex)
			{
				this.loadedFrames.Remove(frame);
				if (this.disposed || !this.trackedFrames.Contains(frame))
				{
					Debug.WriteLine($"WebView2 frame changed while applying CSS: {ex.GetType().Name}");
					return;
				}
				this.ReportError($"ゲームiframeへのCSS適用に失敗しました: {ex.GetType().Name}: {ex.Message}");
			}
		}

		private void HandleFrameDestroyed(object? sender, object e)
		{
			if (sender is not CoreWebView2Frame frame) return;
			// Destroyed通知後はフレームを操作せず、管理情報だけを破棄します。
			this.trackedFrames.Remove(frame);
			this.loadedFrames.Remove(frame);
			this.frameSources.Remove(frame);
		}

		private void ResetTrackedFrames()
		{
			foreach (var frame in this.trackedFrames.ToArray())
			{
				if (frame.IsDestroyed() != 0) continue;
				frame.FrameCreated -= this.HandleNestedFrameCreated;
				frame.NavigationStarting -= this.HandleFrameNavigationStarting;
				frame.DOMContentLoaded -= this.HandleFrameDOMContentLoaded;
				frame.Destroyed -= this.HandleFrameDestroyed;
			}
			this.trackedFrames.Clear();
			this.loadedFrames.Clear();
			this.frameSources.Clear();
		}

		private async void HandleDOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
		{
			// 中継先や子フレームの読み込み完了を待たずに、周辺のページ要素を非表示にします。
			await this.ApplyUserStyleSheetToCurrentDocumentsAsync();
		}

		private async void HandleNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
		{
			if (this.Navigator is not null) this.Navigator.IsNavigating = false;
			this.UpdateNavigator();
			if (!e.IsSuccess)
			{
				this.ReportError($"ページの読み込みに失敗しました: {e.WebErrorStatus}");
				return;
			}
			await this.ApplyUserStyleSheetToCurrentDocumentsAsync();
		}

		private void ApplyUserStyleSheetToCurrentDocuments()
		{
			if (this.ready && !this.disposed) _ = this.ApplyUserStyleSheetToCurrentDocumentsAsync();
		}

		private async Task ApplyUserStyleSheetToCurrentDocumentsAsync()
		{
			if (this.disposed || !this.ready || this.core is null) return;
			var script = CreateUserStyleSheetScript(this.UserStyleSheet ?? string.Empty);
			try
			{
				var result = await this.core.ExecuteScriptAsync(script);
				if (result != "true") throw new InvalidOperationException("スタイル要素を作成できませんでした。");
			}
			catch (Exception ex)
			{
				if (!this.disposed) this.ReportError($"メインページへのCSS適用に失敗しました: {ex.GetType().Name}: {ex.Message}");
			}

			foreach (var frame in this.loadedFrames.ToArray())
			{
				if (!this.trackedFrames.Contains(frame)
					|| !this.loadedFrames.Contains(frame)
					|| !this.frameSources.TryGetValue(frame, out var source)
					|| !WebView2HttpCapture.IsGamePageUrl(source))
				{
					continue;
				}
				try
				{
					await this.ApplyUserStyleSheetToFrameAsync(frame);
				}
				catch (Exception ex)
				{
					if (!this.disposed && this.trackedFrames.Contains(frame))
						this.ReportError($"ゲームiframeへのCSS適用に失敗しました: {ex.GetType().Name}: {ex.Message}");
				}
			}
		}

		private async Task ApplyUserStyleSheetToFrameAsync(CoreWebView2Frame frame)
		{
			if (this.disposed || !this.ready || !this.trackedFrames.Contains(frame) || frame.IsDestroyed() != 0) return;
			var result = await frame.ExecuteScriptAsync(CreateUserStyleSheetScript(this.UserStyleSheet ?? string.Empty));
			if (result != "true") throw new InvalidOperationException("スタイル要素を作成できませんでした。");
		}

		private static string CreateUserStyleSheetScript(string css)
		{
			var styleId = JsonSerializer.Serialize("kancolleviewer-user-style-sheet");
			var serializedCss = JsonSerializer.Serialize(css);
			return $$"""
				(() => {
					const id = {{styleId}};
					document.getElementById(id)?.remove();
					const css = {{serializedCss}};
					if (!css) return true;
					const style = document.createElement('style');
					style.id = id;
					style.textContent = css;
					(document.head || document.documentElement).appendChild(style);
					return document.getElementById(id) === style;
				})()
				""";
		}

		private void HandleWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
		{
			if (this.disposed) return;
			var cancellationToken = this.captureCancellation.Token;
			var safePath = "/";
			try
			{
				var request = e.Request;
				var requestUrl = request.Uri;
				if (WebView2HttpCapture.IsMaintenanceImage(requestUrl))
				{
					this.ShowMaintenanceImage(requestUrl);
					return;
				}
				if (!WebView2HttpCapture.TryGetGameApiUri(requestUrl, out var uri)) return;

				safePath = uri.AbsolutePath;
				var method = request.Method ?? string.Empty;
				var response = e.Response;
				var statusCode = response.StatusCode;
				// 204には本文がなく、リダイレクト応答の本文はWebView2から取得できません。
				if (statusCode == 204 || (statusCode >= 300 && statusCode <= 399)) return;
				var responseHeaders = WebView2HttpCapture.SnapshotResponseHeaders(response.Headers);
				// WebView2 のイベント情報はUIスレッドで取得し、要求本文の読み取りはバックグラウンドへ移します。
				var requestContent = request.Content;
				var requestCaptureTask = this.CaptureRequestBodyAsync(requestContent, method, safePath, cancellationToken);
				var responseContentTask = response.GetContentAsync();
				var captureTask = this.CaptureResponseAsync(
					responseContentTask, requestCaptureTask, requestUrl, method, statusCode, responseHeaders, safePath, cancellationToken);
				this.QueueCapturedResponse(captureTask, safePath, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				Debug.WriteLine("WebView2 API capture was cancelled during shutdown.");
			}
			catch (Exception ex)
			{
				this.ReportCaptureIssue(safePath, $"通信イベントを取得できません ({ex.GetType().Name})", cancellationToken);
			}
		}

		private async Task<WebView2HttpCapture.BodyCapture> CaptureRequestBodyAsync(
			Stream? requestContent,
			string method,
			string safePath,
			CancellationToken cancellationToken)
		{
			var requestCapture = await Task.Run(
				() => WebView2HttpCapture.CaptureRequestBody(requestContent, cancellationToken), cancellationToken).ConfigureAwait(false);
			if (requestCapture.Error is not null)
				this.ReportCaptureIssue(safePath, $"要求本文を取得できません ({requestCapture.Error})", cancellationToken);
			else if (requestCapture.IsTruncated)
				this.ReportCaptureIssue(safePath, "要求本文が5 MiBを超えています", cancellationToken);
			else if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
				&& (!requestCapture.HasStream || requestCapture.Bytes is { Length: 0 }))
				this.ReportCaptureIssue(safePath, "POST要求本文を取得できませんでした（空またはストリームなし）", cancellationToken);

			return requestCapture;
		}

		private async Task<CapturedHttp?> CaptureResponseAsync(
			Task<Stream> responseContentTask,
			Task<WebView2HttpCapture.BodyCapture> requestCaptureTask,
			string url,
			string method,
			int statusCode,
			IDictionary<string, string> responseHeaders,
			string safePath,
			CancellationToken cancellationToken)
		{
			Stream? responseStream = null;
			try
			{
				responseStream = await responseContentTask.ConfigureAwait(false);
				if (responseStream is null)
				{
					this.ReportCaptureIssue(safePath, "応答本文がありません", cancellationToken);
					return null;
				}
				if (cancellationToken.IsCancellationRequested) return null;

				var stream = responseStream;
				// 応答ストリームはスレッドセーフなため、UIスレッド外で読み取ります。
				var responseCapture = await Task.Run(
					() => WebView2HttpCapture.ReadBoundedBodyAsync(stream, cancellationToken), cancellationToken).ConfigureAwait(false);
				if (responseCapture.IsTruncated)
				{
					this.ReportCaptureIssue(safePath, "応答本文が5 MiBを超えています", cancellationToken);
					return null;
				}
				if (responseCapture.Error is not null || responseCapture.Bytes is null)
				{
					this.ReportCaptureIssue(safePath, "応答本文を取得できません", cancellationToken);
					return null;
				}

				var responseBytes = responseCapture.Bytes;
				var responseBody = await Task.Run(
					() => WebView2HttpCapture.DecodeResponseBody(responseBytes, responseHeaders), cancellationToken).ConfigureAwait(false);
				if (string.IsNullOrEmpty(responseBody))
				{
					this.ReportCaptureIssue(safePath, "応答本文が空です", cancellationToken);
					return null;
				}

				var requestCapture = await requestCaptureTask.ConfigureAwait(false);
				var requestBody = requestCapture.Bytes is null
					? string.Empty
					: WebView2HttpCapture.DecodeRequestBody(requestCapture.Bytes);
				return new CapturedHttp
				{
					Url = url,
					Method = method,
					StatusCode = statusCode,
					RequestBody = requestBody,
					ResponseBody = responseBody,
					ResponseHeaders = responseHeaders,
				};
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				Debug.WriteLine("WebView2 response capture was cancelled during shutdown.");
				return null;
			}
			catch (Exception ex)
			{
				this.ReportCaptureIssue(safePath, $"応答本文を処理できません ({ex.GetType().Name})", cancellationToken);
				return null;
			}
			finally
			{
				responseStream?.Dispose();
			}
		}

		private void QueueCapturedResponse(Task<CapturedHttp?> captureTask, string safePath, CancellationToken cancellationToken)
		{
			lock (this.captureQueueLock)
			{
				this.captureQueue = this.ProcessCapturedInOrderAsync(this.captureQueue, captureTask, safePath, cancellationToken);
			}
		}

		private async Task ProcessCapturedInOrderAsync(
			Task previousCapture,
			Task<CapturedHttp?> captureTask,
			string safePath,
			CancellationToken cancellationToken)
		{
			try
			{
				await previousCapture.ConfigureAwait(false);
				var captured = await captureTask.ConfigureAwait(false);
				if (captured is null || cancellationToken.IsCancellationRequested) return;

				await Task.Run(
					() => KanColleClient.Current.ProcessCaptured(captured.Url, captured.ResponseBody, captured.RequestBody),
					cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				Debug.WriteLine("WebView2 ordered API processing was cancelled during shutdown.");
			}
			catch (Exception ex)
			{
				this.ReportCaptureIssue(safePath, $"APIデータを処理できません ({ex.GetType().Name})", cancellationToken);
			}
		}

		// 通知が続かないよう、ステータスバーには最初の1件だけを表示します。2件目以降はデバッグ出力だけに記録します。
		private void ReportCaptureIssue(string safePath, string reason, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested) return;
			var failureCount = Interlocked.Increment(ref this.captureFailureCount);
			var text = $"WebView2のゲーム通信を取得できません: {safePath} ({reason})";
			Debug.WriteLine(text);
			if (failureCount != 1) return;
			this.Dispatcher.BeginInvoke(new Action(() =>
			{
				if (this.disposed || cancellationToken.IsCancellationRequested) return;
				StatusService.Current.Notify(text);
			}));
		}

		private void ShowMaintenanceImage(string url)
		{
			if (this.disposed || this.maintenanceNavigationStarted) return;
			this.maintenanceNavigationStarted = true;
			this.Dispatcher.BeginInvoke(new Action(() =>
			{
				if (this.disposed || !this.ready) return;
				try
				{
					this.browser.CoreWebView2.Navigate(url);
				}
				catch (Exception ex)
				{
					this.ReportError($"メンテナンス画像を表示できません: {ex.GetType().Name}");
				}
			}));
		}

		private void HandleServerCertificateErrorDetected(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
		{
			var allow = NetworkSettings.Relay.IsEnabled.Value
				&& RelayHttpProxyService.IsRunning
				&& KanColleServerOrigin.IsValid(e.RequestUri);
			e.Action = allow
				? CoreWebView2ServerCertificateErrorAction.AlwaysAllow
				: CoreWebView2ServerCertificateErrorAction.Cancel;
			if (!allow && Uri.TryCreate(e.RequestUri, UriKind.Absolute, out var uri)
				&& KanColleServerOrigin.IsAllowedHost(uri.Host))
			{
				this.ReportError("中継プロキシの証明書エラーを許可できませんでした。設定を確認してください。");
			}
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

		private async void HandleScreenshotRequested(object? sender, ScreenshotRequestedEventArgs e)
		{
			if (this.disposed) return;
			try
			{
				if (!this.ready || this.core is null)
					throw new InvalidOperationException("WebView2が撮影可能な状態ではありません。");
				if (!this.loadedFrames.Any(frame => this.frameSources.TryGetValue(frame, out var source)
					&& WebView2HttpCapture.IsGamePageUrl(source)))
				{
					throw new InvalidOperationException("正規ゲームiframeの読み込み完了を待ってから撮影してください。");
				}
				await WebView2ScreenshotCapture.CaptureAndSaveAsync(
					this.core,
					this.loadedFrames,
					e.Path,
					(SupportedImageFormat)e.Format);
				StatusService.Current.Notify(AppResources.Screenshot_Saved + Path.GetFileName(e.Path));
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
				if (!this.disposed) StatusService.Current.Notify(AppResources.Screenshot_Failed + ex.Message);
			}
		}

		private void HandleDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
		{
			e.Cancel = true;
			this.ReportError("ダウンロードを拒否しました。");
		}

		private void HandleNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
		{
			e.Handled = true;
			this.ReportError("新しいウィンドウを開く要求には対応していません。");
		}

		private void HandleProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
		{
			if (this.disposed) return;

			switch (e.ProcessFailedKind)
			{
				case CoreWebView2ProcessFailedKind.BrowserProcessExited:
				case CoreWebView2ProcessFailedKind.RenderProcessExited:
				case CoreWebView2ProcessFailedKind.FrameRenderProcessExited:
					break;
				case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
					this.ReportError("WebView2が応答していません。応答が戻るまでお待ちください。改善しない場合はアプリを再起動してください。");
					return;
				default:
					this.ReportError($"WebView2のプロセス障害: {e.ProcessFailedKind}。WebView2の自動復旧をお待ちください。");
					return;
			}

			this.ready = false;
			if (this.Navigator is not null)
			{
				this.Navigator.IsNavigating = false;
				this.Navigator.CanGoBack = false;
				this.Navigator.CanGoForward = false;
			}
			this.ReportFatalError($"WebView2のプロセス障害: {e.ProcessFailedKind}。アプリを再起動してください。");
		}

		// AreDevToolsEnabled（起動時の設定）は F12 などのキー操作だけを制御し、OpenDevToolsWindow は制限しません。ボタンからの表示は現在の設定で判断します。
		public void ShowDevTools() => this.RunBrowserAction(() =>
		{
			if (!GeneralSettings.IsRemoteDebuggingEnabled.Value)
				throw new InvalidOperationException("開発者向けオプションを有効にしてください。");
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
			var browserSize = new Size(KanColleSize.Width * this.ZoomFactor, KanColleSize.Height * this.ZoomFactor);
			this.browser.Width = browserSize.Width;
			this.browser.Height = browserSize.Height;
			if (this.ready) this.RunBrowserAction(() => this.browser.ZoomFactor = this.ZoomFactor);
			if (this.IsLoaded) this.OwnerSizeChangeRequested?.Invoke(this, browserSize);
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
			StatusService.Current.Notify(text);
			Debug.WriteLine(text);
		}

		private void ReportFatalError(string text)
		{
			this.message.Text = text;
			this.message.Visibility = Visibility.Visible;
			this.ReportError(text);
		}

		private void HandleOwnerClosed(object? sender, EventArgs e) => this.Dispose();

		public void Dispose()
		{
			if (this.disposed) return;
			this.disposed = true;
			this.ready = false;
			this.captureCancellation.Cancel();
			if (this.core is not null)
			{
				this.core.FrameCreated -= this.HandleFrameCreated;
				this.core.DOMContentLoaded -= this.HandleDOMContentLoaded;
				this.core.WebResourceResponseReceived -= this.HandleWebResourceResponseReceived;
				this.core.ServerCertificateErrorDetected -= this.HandleServerCertificateErrorDetected;
				this.core = null;
			}
			this.ResetTrackedFrames();
			this.pendingUri = null;
			this.Loaded -= this.HandleLoaded;
			if (this.owner is not null) this.owner.Closed -= this.HandleOwnerClosed;
			this.ChangeNavigator(this.Navigator, null);
			this.ChangeViewModel(this.ViewModel, null);
			this.muteSubscription?.Dispose();
			this.browser.Dispose();
		}

		// WebView2CompositionControl を通常の ScrollViewer に入れると、クリック時に ScrollViewer が焦点を奪って文字入力できず、
		// 矢印・Home・End・PageUp・PageDown もスクロールや焦点移動に使われてゲームへ届きません。
		// https://github.com/MicrosoftEdge/WebView2Feedback/issues/5057
		private sealed class BrowserViewport : ScrollViewer
		{
			public BrowserViewport()
			{
				this.Focusable = false;
				// PageUp/PageDown は OnKeyDown より先に ScrollViewer 既定のスクロールコマンドで処理されるため、割り当てを無効にします。
				this.InputBindings.Add(new KeyBinding(ApplicationCommands.NotACommand, Key.PageUp, ModifierKeys.None));
				this.InputBindings.Add(new KeyBinding(ApplicationCommands.NotACommand, Key.PageDown, ModifierKeys.None));
			}

			protected override void OnKeyDown(KeyEventArgs e)
			{
			}
		}
	}
}
