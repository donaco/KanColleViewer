using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CefSharp;
using CefSharp.DevTools.Page;
using CefSharp.Wpf;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Cef;
using Grabacr07.KanColleViewer.Properties;
using MetroTrilithon.Mvvm;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	/// <summary>
	/// 艦これのゲーム部分を画像として保存する機能を提供します。
	/// </summary>
	internal class ScreenshotAction : Behavior<ChromiumWebBrowser>
	{
		#region ViewModel 依存関係プロパティ

		public WindowViewModel ViewModel
		{
			get { return (WindowViewModel)this.GetValue(ViewModelProperty); }
			set { this.SetValue(ViewModelProperty, value); }
		}
		public static readonly DependencyProperty ViewModelProperty =
			DependencyProperty.Register(nameof(ViewModel), typeof(WindowViewModel), typeof(ScreenshotAction), new UIPropertyMetadata(null, OnViewModelChanged));

		private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var action = (ScreenshotAction)d;
			if (e.OldValue is WindowViewModel old) old.ScreenshotRequested -= action.OnScreenshotRequested;
			if (e.NewValue is WindowViewModel vm) vm.ScreenshotRequested += action.OnScreenshotRequested;
		}

		#endregion

		protected override void OnDetaching()
		{
			if (this.ViewModel != null) this.ViewModel.ScreenshotRequested -= this.OnScreenshotRequested;
			base.OnDetaching();
		}

		private async void OnScreenshotRequested(object sender, ScreenshotRequestedEventArgs e)
		{
			try
			{
				await this.TakeScreenshot(e.Path, (SupportedImageFormat)e.Format);
				StatusService.Current.Notify(Resources.Screenshot_Saved + Path.GetFileName(e.Path));
			}
			catch (Exception ex)
			{
				StatusService.Current.Notify(Resources.Screenshot_Failed + ex.Message);
			}
		}

		private async Task TakeScreenshot(string path, SupportedImageFormat format)
		{
			var browser = this.AssociatedObject;
			if (browser == null)
			{
				throw new Exception("ブラウザーが見つかりません。");
			}

			var cefBrowser = browser.GetBrowser();
			if (cefBrowser == null)
			{
				throw new Exception("ブラウザーが初期化されていません。");
			}

			// ゲームフレーム (kcs2 の iframe) の表示領域をメインフレームの JS から取得してクリップ領域を決定する
			Viewport clip = null;
			try
			{
				var rectResult = await browser.EvaluateScriptAsync(@"
(function() {
	var iframe = document.querySelector('iframe');
	if (!iframe) return null;
	var r = iframe.getBoundingClientRect();
	if (r.width === 0 || r.height === 0) return null;
	var dpr = window.devicePixelRatio || 1;
	return { x: r.left, y: r.top, width: r.width, height: r.height, scale: dpr };
})();");
				if (rectResult != null && rectResult.Success && rectResult.Result is IDictionary<string, object> rect)
				{
					clip = new Viewport
					{
						X = Convert.ToDouble(rect["x"]),
						Y = Convert.ToDouble(rect["y"]),
						Width = Convert.ToDouble(rect["width"]),
						Height = Convert.ToDouble(rect["height"]),
						Scale = Convert.ToDouble(rect["scale"]),
					};
				}
			}
			catch
			{
				// クリップ取得に失敗した場合はページ全体をキャプチャする
			}

			// CDP Page.CaptureScreenshot で GPU レンダリング済みの画面を直接取得する
			// WebGL の preserveDrawingBuffer: false 問題を回避できる
			var captureFormat = format == SupportedImageFormat.Jpeg
				? CaptureScreenshotFormat.Jpeg
				: CaptureScreenshotFormat.Png;

			CaptureScreenshotResponse screenshotResponse;
			using (var devTools = cefBrowser.GetDevToolsClient())
			{
				var pageClient = devTools.Page;
				screenshotResponse = await pageClient.CaptureScreenshotAsync(captureFormat, clip: clip);
			}

			if (screenshotResponse == null || screenshotResponse.Data == null || screenshotResponse.Data.Length == 0)
			{
				throw new Exception("スクリーンショットのデータが取得できませんでした。");
			}

			await this.SaveScreenshot(path, screenshotResponse.Data);
		}

		private Task SaveScreenshot(string path, byte[] data)
		{
			return Task.Run(() =>
			{
				var directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}

				using (var fs = new FileStream(path, FileMode.Create))
				{
					fs.Write(data, 0, data.Length);
				}
			});
		}
	}
}
