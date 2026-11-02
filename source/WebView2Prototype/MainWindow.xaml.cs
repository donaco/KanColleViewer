using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Grabacr07.KanColleWrapper;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace KanColleViewer.WebView2Prototype;

public partial class MainWindow : Window
{
	private const int MaxCapturedBodyBytes = 5 * 1024 * 1024;
	private const int MaximumLogEntries = 200;
	private const double TargetScreenshotWidth = 1200;
	private const double TargetScreenshotHeight = 720;
	private readonly string userDataFolder = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Grabacr07", "KanColleViewer", "WebView2Prototype");
	private CoreWebView2Environment? browserEnvironment;
	private readonly HashSet<CoreWebView2Frame> loadedFrames = new();
	private bool initialized;

	public MainWindow()
	{
		InitializeComponent();
		ZoomText.Text = $"{ZoomSlider.Value:P0}";
		ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		AddressBox.Focus();
		AddressBox.SelectAll();
	}

	private void Window_Closed(object? sender, EventArgs e)
	{
		Browser.Dispose();
	}

	private async void StartButton_Click(object sender, RoutedEventArgs e)
	{
		if (!TryGetNavigationUri(out var uri))
		{
			return;
		}

		StartButton.IsEnabled = false;
		try
		{
			if (initialized)
			{
				Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
				return;
			}

			string runtimeVersion;
			try
			{
				runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
			}
			catch (WebView2RuntimeNotFoundException)
			{
				throw new InvalidOperationException("Evergreen WebView2 Runtime が見つかりません。Runtime を導入してから試作を起動してください。");
			}

			Directory.CreateDirectory(userDataFolder);
			if (browserEnvironment is null)
			{
				var environmentOptions = new CoreWebView2EnvironmentOptions
				{
					AdditionalBrowserArguments = GetBrowserArguments(),
				};
				browserEnvironment = await CoreWebView2Environment.CreateAsync(
					browserExecutableFolder: null,
					userDataFolder: userDataFolder,
					options: environmentOptions);
				FrameRateModeBox.IsEnabled = false;
				DisableGpuBox.IsEnabled = false;
			}
			var environment = browserEnvironment
				?? throw new InvalidOperationException("WebView2 環境が作成されていません。");
			await Browser.EnsureCoreWebView2Async(environment);

			RegisterBrowserEvents();
			initialized = true;
			Browser.Visibility = Visibility.Visible;
			BackButton.IsEnabled = true;
			ForwardButton.IsEnabled = true;
			RefreshButton.IsEnabled = true;
			ScreenshotButton.IsEnabled = true;
			DevToolsButton.IsEnabled = true;
			ZoomSlider.IsEnabled = true;
			StartButton.Content = "移動";
			Browser.ZoomFactor = ZoomSlider.Value;
			Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
			var frameRateMode = FrameRateModeBox.SelectedIndex switch
			{
				2 => "上限解除: --disable-frame-rate-limit を指定（効果未検証）",
				_ => "既定動作: FPS・モニター同期は保証しません",
			};
			var gpuMode = DisableGpuBox.IsChecked == true ? "; --disable-gpu を適用" : string.Empty;
			StatusText.Text = $"Runtime {runtimeVersion}; {frameRateMode}{gpuMode}; UDF は CEF と分離されています。";
			AddLog("WebView2 初期化完了。通信本文は画面に表示・ファイル保存せず、5 MiB 上限で読み取り量のみ記録します。");
		}
		catch (Exception ex)
		{
			StatusText.Text = $"初期化に失敗しました: {ex.GetType().Name}: {ex.Message}";
			AddLog($"初期化失敗: {ex.GetType().Name}");
		}
		finally
		{
			StartButton.IsEnabled = true;
		}
	}

	private void RegisterBrowserEvents()
	{
		var core = Browser.CoreWebView2;
		core.NavigationStarting += (_, _) => StatusText.Text = "ページを読み込み中です。";
		core.NavigationCompleted += (_, args) =>
		{
			AddressBox.Text = core.Source;
			BackButton.IsEnabled = core.CanGoBack;
			ForwardButton.IsEnabled = core.CanGoForward;
			StatusText.Text = args.IsSuccess
				? $"表示中: {GetSafeUriPath(core.Source)}"
				: $"ナビゲーションに失敗しました: {args.WebErrorStatus}";
		};
		core.FrameCreated += (_, args) =>
		{
			var frame = args.Frame;
			frame.DOMContentLoaded += (_, _) => loadedFrames.Add(frame);
			frame.NavigationStarting += (_, _) => loadedFrames.Remove(frame);
			frame.Destroyed += (_, _) => loadedFrames.Remove(frame);
		};
		core.WebResourceResponseReceived += Browser_WebResourceResponseReceived;
		core.DownloadStarting += (_, args) =>
		{
			args.Cancel = true;
			AddLog("ダウンロードを試作アプリ側で拒否しました。");
		};
		core.NewWindowRequested += (_, args) =>
		{
			args.Handled = true;
			AddLog("新しいウィンドウを開く要求を拒否しました。");
		};
		core.ProcessFailed += (_, args) =>
		{
			StatusText.Text = $"WebView2 プロセス障害: {args.ProcessFailedKind}";
			AddLog($"WebView2 プロセス障害: {args.ProcessFailedKind}");
		};
	}

	private string GetBrowserArguments()
	{
		var arguments = new List<string>();
		if (FrameRateModeBox.SelectedIndex == 2)
		{
			arguments.Add("--disable-frame-rate-limit");
		}
		if (DisableGpuBox.IsChecked == true)
		{
			arguments.Add("--disable-gpu");
		}
		return string.Join(" ", arguments);
	}

	private bool TryGetNavigationUri(out Uri uri)
	{
		if (Uri.TryCreate(AddressBox.Text, UriKind.Absolute, out var parsed)
			&& (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
		{
			uri = parsed;
			return true;
		}

		uri = null!;
		StatusText.Text = "http または https の URL を入力してください。";
		return false;
	}

	private async void Browser_WebResourceResponseReceived(
		object? sender,
		CoreWebView2WebResourceResponseReceivedEventArgs args)
	{
		var requestUri = args.Request.Uri;
		if (!KanColleServerOrigin.IsValid(requestUri)
			|| !Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)
			|| !uri.AbsolutePath.StartsWith("/kcsapi/", StringComparison.Ordinal))
		{
			return;
		}

		var safePath = uri.AbsolutePath;
		var requestBody = CaptureRequestBody(args.Request.Content);
		try
		{
			using var responseStream = await args.Response.GetContentAsync();
			if (responseStream is null)
			{
				AddLog($"{args.Request.Method} {safePath} → {args.Response.StatusCode}; 応答本文なし; {FormatBodyResult(requestBody, "request")}");
				return;
			}

			var responseBody = await Task.Run(() => ReadBoundedBody(responseStream));
			AddLog($"{args.Request.Method} {safePath} → {args.Response.StatusCode}; {FormatBodyResult(requestBody, "request")}; {FormatBodyResult(responseBody, "response")}");
		}
		catch (Exception ex)
		{
			AddLog($"{args.Request.Method} {safePath} → 応答本文取得失敗: {ex.GetType().Name}; {FormatBodyResult(requestBody, "request")}");
		}
	}

	private static BodyReadResult CaptureRequestBody(Stream? stream)
	{
		if (stream is null)
		{
			return new BodyReadResult(0, false, "本文なし");
		}
		try
		{
			if (!stream.CanSeek)
			{
				return new BodyReadResult(0, false, "ストリームが seek 非対応のため未取得");
			}

			var originalPosition = stream.Position;
			BodyReadResult result;
			try
			{
				stream.Position = 0;
				result = ReadBoundedBody(stream);
			}
			catch (Exception ex)
			{
				result = new BodyReadResult(0, false, $"取得失敗 ({ex.GetType().Name})");
			}

			try
			{
				stream.Position = originalPosition;
			}
			catch (Exception ex)
			{
				return new BodyReadResult(result.ByteCount, result.Truncated, $"{result.Availability}; 元の位置へ復元失敗 ({ex.GetType().Name})");
			}
			return result;
		}
		catch (Exception ex)
		{
			return new BodyReadResult(0, false, $"ストリーム確認失敗 ({ex.GetType().Name})");
		}
	}

	private static BodyReadResult ReadBoundedBody(Stream stream)
	{
		var buffer = new byte[16 * 1024];
		var totalBytes = 0;
		var limit = MaxCapturedBodyBytes + 1;
		while (totalBytes < limit)
		{
			var bytesRead = stream.Read(buffer, 0, Math.Min(buffer.Length, limit - totalBytes));
			if (bytesRead == 0)
			{
				break;
			}
			totalBytes += bytesRead;
		}

		var truncated = totalBytes > MaxCapturedBodyBytes;
		return new BodyReadResult(Math.Min(totalBytes, MaxCapturedBodyBytes), truncated, "取得");
	}

	private static string FormatBodyResult(BodyReadResult result, string kind)
	{
		if (result.Availability != "取得")
		{
			return $"{kind} 本文: {result.Availability}";
		}
		var suffix = result.Truncated ? "以上（5 MiB 上限）" : "";
		return $"{kind} 本文: {result.ByteCount:N0} bytes{suffix}";
	}

	private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
	{
		ScreenshotButton.IsEnabled = false;
		try
		{
			var dialog = new SaveFileDialog
			{
				Filter = "PNG 画像 (*.png)|*.png|JPEG 画像 (*.jpg)|*.jpg",
				DefaultExt = ".png",
				AddExtension = true,
				FileName = "KanColle-WebView2.png",
			};
			if (dialog.ShowDialog(this) != true)
			{
				return;
			}

			var isJpeg = string.Equals(Path.GetExtension(dialog.FileName), ".jpg", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(Path.GetExtension(dialog.FileName), ".jpeg", StringComparison.OrdinalIgnoreCase);
			var resultJson = await CaptureGameScreenshotAsync(isJpeg);
			var result = JsonSerializer.Deserialize<ScreenshotResult>(
				resultJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
				?? throw new InvalidOperationException("DevTools Protocol から撮影データが返りませんでした。");
			var imageBytes = Convert.FromBase64String(result.Data);
			var dimensions = ReadImageDimensions(imageBytes);
			if (dimensions.Width != TargetScreenshotWidth || dimensions.Height != TargetScreenshotHeight)
			{
				throw new InvalidOperationException($"撮影寸法が要件と異なります: {dimensions.Width}×{dimensions.Height} px (期待値 1200×720 px)");
			}

			await File.WriteAllBytesAsync(dialog.FileName, imageBytes);
			StatusText.Text = $"ゲーム iframe を {dimensions.Width}×{dimensions.Height} px で保存しました。";
			AddLog($"ゲーム iframe 撮影: {dimensions.Width}×{dimensions.Height} px ({(isJpeg ? "JPEG" : "PNG")})");
		}
		catch (Exception ex)
		{
			StatusText.Text = $"ゲーム画面の撮影に失敗しました: {ex.GetType().Name}: {ex.Message}";
			AddLog($"撮影失敗: {ex.GetType().Name}");
		}
		finally
		{
			ScreenshotButton.IsEnabled = true;
		}
	}

	private async Task<string> CaptureGameScreenshotAsync(bool isJpeg)
	{
		var core = Browser.CoreWebView2;
		var styleId = JsonSerializer.Serialize($"webview2-screenshot-{Guid.NewGuid():N}");
		try
		{
			// Remove scrollbar layout space before measuring centered content.
			var prepareResult = await core.ExecuteScriptAsync($$"""
				(() => {
					const style = document.createElement('style');
					style.id = {{styleId}};
					style.textContent = 'html { scrollbar-gutter: auto !important; } html::-webkit-scrollbar, body::-webkit-scrollbar { display: none !important; }';
					document.documentElement.appendChild(style);
					return document.documentElement.getBoundingClientRect().width > 0;
				})()
				""");
			if (prepareResult != "true")
			{
				throw new InvalidOperationException("撮影用CSSを適用できませんでした。");
			}
			var frame = await FindGameFrameAsync();
			var clip = CreateScreenshotClip(frame);
			var screenshotParameters = new Dictionary<string, object>
			{
				["format"] = isJpeg ? "jpeg" : "png",
				["clip"] = new { x = clip.X, y = clip.Y, width = clip.Width, height = clip.Height, scale = clip.Scale },
				["fromSurface"] = true,
				["captureBeyondViewport"] = true,
			};
			if (isJpeg)
			{
				screenshotParameters["quality"] = 90;
			}
			await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}");
			return await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", JsonSerializer.Serialize(screenshotParameters));
		}
		finally
		{
			var restoreResult = await core.ExecuteScriptAsync($$"""
				(() => {
					document.getElementById({{styleId}})?.remove();
					return !document.getElementById({{styleId}});
				})()
				""");
			if (restoreResult != "true")
			{
				throw new InvalidOperationException("撮影用CSSを解除できませんでした。ページを更新してください。");
			}
		}
	}

	private async Task<FrameBounds> FindGameFrameAsync()
	{
		const string script = """
			(() => {
				const frames = Array.from(document.querySelectorAll('iframe')).filter(item =>
					new URL(item.src || 'about:blank', document.baseURI).pathname === '/kcs2/index.php');
				if (frames.length !== 1) return null;
				const frame = frames[0];
				const rect = frame.getBoundingClientRect();
				if (rect.width <= 0 || rect.height <= 0) return null;
				// CDP uses page coordinates; nested frame coordinates stay relative to their viewport.
				return { x: rect.x + (window === window.top ? window.scrollX : 0),
					y: rect.y + (window === window.top ? window.scrollY : 0),
					width: rect.width, height: rect.height,
					gameUrl: frame.src, parentUrl: location.origin + location.pathname };
			})()
			""";
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var result = await Browser.CoreWebView2.ExecuteScriptAsync(script);
		var directFrame = JsonSerializer.Deserialize<FrameBounds?>(result, options);
		if (directFrame is not null && KanColleServerOrigin.IsValid(directFrame.GameUrl))
		{
			return directFrame;
		}

		const string containerScript = """
			(() => {
				const frame = document.querySelector('iframe#game_frame');
				if (!frame) return null;
				const rect = frame.getBoundingClientRect();
				if (rect.width <= 0 || rect.height <= 0 || frame.offsetWidth <= 0 || frame.offsetHeight <= 0) return null;
				const scaleX = rect.width / frame.offsetWidth;
				const scaleY = rect.height / frame.offsetHeight;
				const url = new URL(frame.src, document.baseURI);
				return { x: rect.x + window.scrollX + frame.clientLeft * scaleX,
					y: rect.y + window.scrollY + frame.clientTop * scaleY,
					scaleX, scaleY, url: url.origin + url.pathname };
			})()
			""";
		var containerResult = await Browser.CoreWebView2.ExecuteScriptAsync(containerScript);
		var container = JsonSerializer.Deserialize<FrameContainer?>(containerResult, options)
			?? throw new InvalidOperationException("外側の game_frame が見つかりません。ページ全体は代替撮影しません。");
		var matches = new List<FrameBounds>();
		foreach (var frame in loadedFrames.ToArray())
		{
			if (!loadedFrames.Contains(frame))
			{
				continue;
			}
			var innerResult = await frame.ExecuteScriptAsync(script);
			var inner = JsonSerializer.Deserialize<FrameBounds?>(innerResult, options);
			if (inner is not null && inner.ParentUrl == container.Url && KanColleServerOrigin.IsValid(inner.GameUrl))
			{
				matches.Add(new FrameBounds(
					container.X + inner.X * container.ScaleX,
					container.Y + inner.Y * container.ScaleY,
					inner.Width * container.ScaleX,
					inner.Height * container.ScaleY));
			}
		}
		return matches.Count == 1 ? matches[0]
			: throw new InvalidOperationException("game_frame 内の正規ゲーム iframe を一意に特定できません。読み込み完了後に再試行してください。ページ全体は代替撮影しません。");
	}

	private static ScreenshotClip CreateScreenshotClip(FrameBounds frame)
	{
		var width = frame.Width;
		var height = frame.Height;
		var targetRatio = TargetScreenshotWidth / TargetScreenshotHeight;
		if (width / height > targetRatio)
		{
			var croppedWidth = height * targetRatio;
			return new ScreenshotClip(frame.X + (width - croppedWidth) / 2, frame.Y, croppedWidth, height, TargetScreenshotWidth / croppedWidth);
		}

		var croppedHeight = width / targetRatio;
		return new ScreenshotClip(frame.X, frame.Y + (height - croppedHeight) / 2, width, croppedHeight, TargetScreenshotWidth / width);
	}

	private static (int Width, int Height) ReadImageDimensions(byte[] imageBytes)
	{
		using var stream = new MemoryStream(imageBytes, writable: false);
		var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		var frame = decoder.Frames.FirstOrDefault()
			?? throw new InvalidOperationException("画像データをデコードできませんでした。");
		return (frame.PixelWidth, frame.PixelHeight);
	}

	private static string GetSafeUriPath(string source)
	{
		return Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "URL 不明";
	}

	private void AddLog(string message)
	{
		CaptureLog.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
		while (CaptureLog.Items.Count > MaximumLogEntries)
		{
			CaptureLog.Items.RemoveAt(CaptureLog.Items.Count - 1);
		}
	}

	private void AddressBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && initialized && TryGetNavigationUri(out var uri))
		{
			Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
		}
	}

	private void BackButton_Click(object sender, RoutedEventArgs e)
	{
		if (Browser.CoreWebView2.CanGoBack)
		{
			Browser.CoreWebView2.GoBack();
		}
	}

	private void ForwardButton_Click(object sender, RoutedEventArgs e)
	{
		if (Browser.CoreWebView2.CanGoForward)
		{
			Browser.CoreWebView2.GoForward();
		}
	}

	private void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		Browser.CoreWebView2.Reload();
	}

	private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		ZoomText.Text = $"{e.NewValue:P0}";
		if (initialized)
		{
			Browser.ZoomFactor = e.NewValue;
		}
	}

	private void DevToolsButton_Click(object sender, RoutedEventArgs e)
	{
		Browser.CoreWebView2.OpenDevToolsWindow();
	}

	private void PopupTestButton_Click(object sender, RoutedEventArgs e)
	{
		if (!initialized)
		{
			StatusText.Text = "ゲームを表示してからPopup検証を行ってください。";
			return;
		}
		TestPopup.IsOpen = true;
	}

	private void CloseTestPopupButton_Click(object sender, RoutedEventArgs e)
	{
		TestPopup.IsOpen = false;
		Browser.Focus();
	}

	private sealed record BodyReadResult(int ByteCount, bool Truncated, string Availability);
	private sealed record FrameBounds(double X, double Y, double Width, double Height, string? GameUrl = null, string? ParentUrl = null);
	private sealed record FrameContainer(double X, double Y, double ScaleX, double ScaleY, string Url);
	private sealed record ScreenshotClip(double X, double Y, double Width, double Height, double Scale);
	private sealed record ScreenshotResult(string Data);
}
