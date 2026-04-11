using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Wpf;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Cef;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.ViewModels.Messages;
using Livet.Behaviors.Messaging;
using Livet.Messaging;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	/// <summary>
	/// 艦これのゲーム部分を画像として保存する機能を提供します。
	/// </summary>
	internal class ScreenshotAction : InteractionMessageAction<ChromiumWebBrowser>
	{
		protected override async void InvokeAction(InteractionMessage message)
		{
			if (message is ScreenshotMessage screenshotMessage)
			{
				try
				{
					await this.TakeScreenshot(screenshotMessage.Path, screenshotMessage.Format);
					StatusService.Current.Notify(Resources.Screenshot_Saved + Path.GetFileName(screenshotMessage.Path));
				}
				catch (Exception ex)
				{
					StatusService.Current.Notify(Resources.Screenshot_Failed + ex.Message);
					System.Diagnostics.Debug.WriteLine($"[ScreenshotAction] Exception: {ex}");
				}
			}
		}

		private async Task TakeScreenshot(string path, SupportedImageFormat format)
		{
			var browser = this.AssociatedObject;
			if (browser == null)
			{
				throw new Exception("ブラウザーが見つかりません。");
			}

			System.Diagnostics.Debug.WriteLine("[ScreenshotAction] Taking screenshot...");

			// ゲームフレーム (kcs2 の iframe) を取得
			if (!browser.TryGetKanColleCanvas(out var gameFrame))
			{
				throw new Exception("艦これのゲームフレームが見つかりません。");
			}

			System.Diagnostics.Debug.WriteLine($"[ScreenshotAction] Game frame found: {gameFrame.Url}");

			var mimeType = format.ToMimeType();

			// WebGL の preserveDrawingBuffer 問題を回避するため、
			// requestAnimationFrame 内で描画直後にキャプチャする。
			// Promise を返し、EvaluateScriptAsync で結果を受け取る。
			var jsResult = await gameFrame.EvaluateScriptAsync($@"
new Promise(function(resolve) {{
	var canvas = document.querySelector('canvas');
	if (!canvas) {{
		resolve({{ success: false, error: 'Canvas element not found' }});
		return;
	}}
	if (canvas.width === 0 || canvas.height === 0) {{
		resolve({{ success: false, error: 'Canvas size is zero: ' + canvas.width + 'x' + canvas.height }});
		return;
	}}

	// WebGL バッファを描画直後にキャプチャする
	var gl = canvas.getContext('webgl2') || canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
	if (gl) {{
		// WebGL: readPixels で描画済みバッファからピクセルデータを取得し、
		// 2D canvas に転写して toDataURL する
		requestAnimationFrame(function() {{
			try {{
				var w = canvas.width;
				var h = canvas.height;
				var pixels = new Uint8Array(w * h * 4);
				gl.readPixels(0, 0, w, h, gl.RGBA, gl.UNSIGNED_BYTE, pixels);

				// 一時 canvas で上下反転して PNG/JPEG に変換
				var tmpCanvas = document.createElement('canvas');
				tmpCanvas.width = w;
				tmpCanvas.height = h;
				var ctx = tmpCanvas.getContext('2d');
				var imageData = ctx.createImageData(w, h);

				// WebGL の readPixels は左下原点なので上下反転
				for (var y = 0; y < h; y++) {{
					var srcOffset = (h - y - 1) * w * 4;
					var dstOffset = y * w * 4;
					for (var x = 0; x < w * 4; x++) {{
						imageData.data[dstOffset + x] = pixels[srcOffset + x];
					}}
				}}
				ctx.putImageData(imageData, 0, 0);

				var dataUrl = tmpCanvas.toDataURL('{mimeType}');
				resolve({{ success: true, data: dataUrl }});
			}} catch(e) {{
				resolve({{ success: false, error: 'readPixels error: ' + e.message }});
			}}
		}});
	}} else {{
		// 2D Canvas のフォールバック
		requestAnimationFrame(function() {{
			try {{
				var dataUrl = canvas.toDataURL('{mimeType}');
				resolve({{ success: true, data: dataUrl }});
			}} catch(e) {{
				resolve({{ success: false, error: 'toDataURL error: ' + e.message }});
			}}
		}});
	}}
}});
");

			if (jsResult == null)
			{
				throw new Exception("JavaScript の評価が失敗しました。");
			}

			System.Diagnostics.Debug.WriteLine($"[ScreenshotAction] jsResult.Success: {jsResult.Success}, Result type: {jsResult.Result?.GetType().Name}");

			if (!jsResult.Success)
			{
				throw new Exception($"JavaScript エラー: {jsResult.Message}");
			}

			// CefSharp v145 では ExpandoObject として返される
			if (jsResult.Result is IDictionary<string, object> resultDict)
			{
				if (resultDict.TryGetValue("success", out var successObj) && successObj is bool success)
				{
					if (success && resultDict.TryGetValue("data", out var dataObj))
					{
						var dataUrl = dataObj as string;
						System.Diagnostics.Debug.WriteLine($"[ScreenshotAction] DataURL obtained, length: {dataUrl?.Length ?? 0}");
						await this.SaveScreenshot(path, dataUrl);
						return;
					}
					else if (resultDict.TryGetValue("error", out var errorObj))
					{
						throw new Exception($"JavaScript エラー: {errorObj}");
					}
				}
			}

			throw new Exception("スクリーンショット取得に失敗しました。");
		}

		private Task SaveScreenshot(string path, string dataUrl)
		{
			return Task.Run(() =>
			{
				if (string.IsNullOrEmpty(dataUrl))
				{
					throw new Exception("dataUrl が空です。");
				}

				var array = dataUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length != 2)
				{
					throw new Exception($"無効な形式: {array.Length} 個の要素");
				}

				var base64 = array[1];
				var bytes = Convert.FromBase64String(base64);

				var directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}

				using (var fs = new FileStream(path, FileMode.CreateNew))
				{
					fs.Write(bytes, 0, bytes.Length);
				}

				System.Diagnostics.Debug.WriteLine($"[ScreenshotAction] Screenshot saved: {path}");
			});
		}
	}
}
