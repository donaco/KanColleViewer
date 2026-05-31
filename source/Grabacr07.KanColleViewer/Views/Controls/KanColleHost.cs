using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using CefSharp;
using CefSharp.Wpf;
using CefSharp.Wpf.Internals;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Cef;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleWrapper;

namespace Grabacr07.KanColleViewer.Views.Controls
{
	[ContentProperty(nameof(WebBrowser))]
	[TemplatePart(Name = PART_ContentHost, Type = typeof(ScrollViewer))]
	public class KanColleHost : Control
	{
		private const string PART_ContentHost = "PART_ContentHost";

		public static Size KanColleSize { get; } = new Size(1200.0, 720.0);
		public static Size InitialSize { get; } = new Size(1200.0, 720.0);

		static KanColleHost()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(KanColleHost), new FrameworkPropertyMetadata(typeof(KanColleHost)));
		}

		private ScrollViewer scrollViewer;
		private bool styleSheetApplied;
		private bool focusInInputbox;

		#region WebBrowser 依存関係プロパティ

		public ChromiumWebBrowser WebBrowser
		{
			get => (ChromiumWebBrowser)this.GetValue(WebBrowserProperty);
			set => this.SetValue(WebBrowserProperty, value);
		}

		public static readonly DependencyProperty WebBrowserProperty =
			DependencyProperty.Register(nameof(WebBrowser), typeof(ChromiumWebBrowser), typeof(KanColleHost), new UIPropertyMetadata(null, WebBrowserPropertyChangedCallback));

		private static void WebBrowserPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var instance = (KanColleHost)d;
			var newBrowser = (ChromiumWebBrowser)e.NewValue;
			var oldBrowser = (ChromiumWebBrowser)e.OldValue;

			if (oldBrowser != null)
			{
				oldBrowser.FrameLoadEnd -= instance.HandleLoadEnd;
				oldBrowser.IsBrowserInitializedChanged -= instance.HandleBrowserInitializedChanged;
			}
			if (newBrowser != null)
			{
				newBrowser.FrameLoadEnd += instance.HandleLoadEnd;
				newBrowser.IsBrowserInitializedChanged += instance.HandleBrowserInitializedChanged;
				newBrowser.MenuHandler = new ContextMenuHandler();
				newBrowser.WpfKeyboardHandler = new InhibitTabKeyHandler(newBrowser);
				instance.ApplyMuteIfReady(newBrowser);
			}
			if (instance.scrollViewer != null)
			{
				instance.scrollViewer.Content = newBrowser;
			}
		}

		private void HandleBrowserInitializedChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (!(e.NewValue is bool initialized) || !initialized) return;
			if (!(sender is ChromiumWebBrowser browser)) return;

			this.ApplyMuteIfReady(browser);
		}

		private void ApplyMuteIfReady(ChromiumWebBrowser browser)
		{
			try
			{
				if (browser?.IsBrowserInitialized == true)
				{
					browser.GetBrowser()?.GetHost()?.SetAudioMuted(GeneralSettings.IsMuted.Value);
				}
			}
			catch
			{
			}
		}

		#endregion

		#region ZoomFactor 依存関係プロパティ

		/// <summary>
		/// ブラウザーのズーム倍率を取得または設定します。
		/// </summary>
		public double ZoomFactor
		{
			get => (double)this.GetValue(ZoomFactorProperty);
			set => this.SetValue(ZoomFactorProperty, value);
		}

		/// <summary>
		/// <see cref="ZoomFactor"/> 依存関係プロパティを識別します。
		/// </summary>
		public static readonly DependencyProperty ZoomFactorProperty =
			DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(KanColleHost), new UIPropertyMetadata(1.0, ZoomFactorChangedCallback));

		private static void ZoomFactorChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var instance = (KanColleHost)d;

			instance.ApplySize();
		}

		#endregion

		#region UserStyleSheet 依存関係プロパティ

		/// <summary>
		/// ユーザー スタイル シートを取得または設定します。
		/// </summary>
		public string UserStyleSheet
		{
			get => (string)this.GetValue(UserStyleSheetProperty);
			set => this.SetValue(UserStyleSheetProperty, value);
		}

		/// <summary>
		/// <see cref="UserStyleSheet"/> 依存関係プロパティを識別します。
		/// </summary>
		public static readonly DependencyProperty UserStyleSheetProperty =
			DependencyProperty.Register(nameof(UserStyleSheet), typeof(string), typeof(KanColleHost), new UIPropertyMetadata(string.Empty, UserStyleSheetChangedCallback));

		private static void UserStyleSheetChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var instance = (KanColleHost)d;

			instance.ApplyStyleSheet();
		}

		#endregion

		public event EventHandler<Size> OwnerSizeChangeRequested;

		public KanColleHost()
		{
			this.Loaded += (sender, args) => this.ApplySize();
		}

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			this.scrollViewer = this.GetTemplateChild(PART_ContentHost) as ScrollViewer;
			if (this.scrollViewer != null)
			{
				this.scrollViewer.Content = this.WebBrowser;
			}
		}

		public void ApplySize()
		{
			if (this.WebBrowser == null) return;

			this.ApplyZoomFactor(this.ZoomFactor);

			var baseSize = this.styleSheetApplied ? KanColleSize : InitialSize;
			var size = new Size(
				(baseSize.Width * this.ZoomFactor),
				(baseSize.Height * this.ZoomFactor));

			this.WebBrowser.Width = size.Width;
			this.WebBrowser.Height = size.Height;

			this.OwnerSizeChangeRequested?.Invoke(this, size);
		}

		private void ApplyZoomFactor(double zoomFactor)
		{
			if (zoomFactor < 0.1 || zoomFactor > 10)
			{
				StatusService.Current.Notify(string.Format(Properties.Resources.ZoomAction_OutOfRange, (int)(zoomFactor * 100)));
				return;
			}

			try
			{
				this.WebBrowser.ZoomLevel = 0;
				this.WebBrowser.ZoomLevel = Math.Log(zoomFactor) / Math.Log(1.2);
			}
			catch (Exception) when (Application.Instance.State == ApplicationState.Startup)
			{
				// about:blank だから仕方ない
			}
			catch (Exception ex)
			{
				StatusService.Current.Notify(string.Format(Properties.Resources.ZoomAction_ZoomFailed, ex.Message));
				System.Diagnostics.Debug.WriteLine(ex);
			}
		}

		private void HandleLoadEnd(object sender, FrameLoadEndEventArgs e)
		{
			// null チェック：e 自体が null またはそのプロパティが null の場合の防御
			if (e == null)
			{
				return;
			}

			try
			{
				// UI スレッドに処理を委譲して、WPF オブジェクトへのアクセス例外を防ぐ
				this.Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						// WebBrowser が準備できていれば RequestHandler を割り当てる
						// null チェックを厳密に（BeginInvoke 時点と実行時点の両方をチェック）
						var browser = this.WebBrowser;
						if (browser != null)
						{
							// AttachRequestHandler は CustomRequestHandler を割り当てます
							// 既に CustomRequestHandler が設定されていなければアタッチ
							try
							{
								if (!(browser.RequestHandler is CustomRequestHandler))
								{
									CefBridge.AttachRequestHandler(browser, captured =>
									{
										try
										{
											// 非 UI スレッドで処理する（CapturedProcessor 側でスレッド安全に扱う）
											Grabacr07.KanColleWrapper.KanColleClient.Current.ProcessCaptured(captured.Url, captured.ResponseBody, captured.RequestBody);
										}
										catch
										{
										}
									});
								}
							}
							catch
							{
							}
						}
					}
					catch
					{
					}
				}));
			}
			catch
			{
			}

			if (e.Frame != null && e.Frame.IsMain)
			{
				this.Dispatcher.Invoke(() =>
				{
					this.ApplySize();
					this.ApplyMuteIfReady(this.WebBrowser);
				});
			}

			if (!string.IsNullOrEmpty(e.Url) && e.Url.Contains("/kcs2/index.php"))
			{
				this.Dispatcher.Invoke(() => this.ApplyStyleSheet());
			}
		}

		/// <summary>
		/// CSS 文字列を JavaScript 文字列リテラルとして安全に埋め込むためにエスケープします。
		/// </summary>
		private static string EscapeForJsString(string value)
		{
			if (string.IsNullOrEmpty(value)) return string.Empty;

			return value
				.Replace("\\", "\\\\")  // \ → \\ （必ず最初に処理）
				.Replace("\"", "\\\"")  // " → \"
				.Replace("\r", "")      // CR 除去
				.Replace("\n", " ");    // LF → スペース
		}

		private void ApplyStyleSheet()
		{
			try
			{
				if (this.WebBrowser == null) return; // null時遅延　エラー回避
				if (this.WebBrowser.TryGetKanColleCanvas(out var canvas))
				{
					var escapedCss = EscapeForJsString(this.UserStyleSheet);
					var js = $"var style = document.createElement(\"style\"); style.innerHTML = \"{escapedCss}\"; document.body.appendChild(style);";
					this.WebBrowser.GetMainFrame().ExecuteJavaScriptAsync(js);
					canvas.ExecuteJavaScriptAsync(js);

					this.styleSheetApplied = true;

					this.RegisterInputFocusHandler(canvas);
				}
			}
			catch (Exception) when (Application.Instance.State == ApplicationState.Startup)
			{
				// about:blank だから仕方ない
			}
			catch (Exception ex)
			{
				StatusService.Current.Notify("failed to apply css: " + ex.Message);
				System.Diagnostics.Debug.WriteLine(ex);
			}
		}


		private void RegisterInputFocusHandler(IFrame frame)
		{
			var handler = new InputboxFocusHandler();
			var script = $@"
(async function()
{{
	await CefSharp.BindObjectAsync('{handler.Id}');

	var input = document.getElementById('r_editbox');
	input.onfocus = function() {{ {handler.Id}.setInputFocus(true); }};
	input.onblur = function() {{ {handler.Id}.setInputFocus(false); }};
}})();
";
			handler.FocusChanged += hasFocus =>
			{
				this.focusInInputbox = hasFocus;
			};

			this.WebBrowser.JavascriptObjectRepository.Register(handler.Id, handler, true);
			frame.ExecuteJavaScriptAsync(script);
		}
	}

	public class InputboxFocusHandler
	{
		public event Action<bool> FocusChanged;

		public string Id { get; }

		public InputboxFocusHandler()
		{
			this.Id = $"focusHandler{DateTimeOffset.Now.Ticks}";
		}

		public void SetInputFocus(bool hasFocus)
		{
			this.FocusChanged?.Invoke(hasFocus);
		}
	}

	public class InhibitTabKeyHandler : WpfKeyboardHandler
	{
		public InhibitTabKeyHandler(ChromiumWebBrowser owner)
			: base(owner)
		{
		}

		public override void HandleKeyPress(KeyEventArgs e)
		{
			if (e.Key == Key.Tab)
			{
				e.Handled = true;
				return;
			}

			base.HandleKeyPress(e);
		}
	}
}
