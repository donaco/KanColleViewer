using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Grabacr07.KanColleViewer.Models;
using Microsoft.Web.WebView2.Core;

namespace Grabacr07.KanColleViewer.Models.WebView2
{
	internal static class WebView2ScreenshotCapture
	{
		private const int TargetScreenshotWidth = 1200;
		private const int TargetScreenshotHeight = 720;
		private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

		internal static async Task CaptureAndSaveAsync(
			CoreWebView2 core,
			ISet<CoreWebView2Frame> loadedFrames,
			string path,
			SupportedImageFormat format)
		{
			if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("保存先が指定されていません。", nameof(path));

			if (format != SupportedImageFormat.Png && format != SupportedImageFormat.Jpeg)
				throw new ArgumentOutOfRangeException(nameof(format), format, "PNGまたはJPEGを指定してください。");

			var frame = await FindGameFrameAsync(core, loadedFrames);
			var metricsJson = await core.ExecuteScriptAsync(
				"({ pixelRatio: window.devicePixelRatio, scrollX: window.scrollX, scrollY: window.scrollY })");
			var metrics = JsonSerializer.Deserialize<CaptureMetrics>(metricsJson, jsonOptions)
				?? throw new InvalidOperationException("撮影用の表示情報を取得できませんでした。");
			using var capture = new MemoryStream();
			// CDP の clip 指定は描画領域を一時変更するため、表示を変えない撮影後に画像側で切り抜きます。
			await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
			var capturedBytes = capture.ToArray();
			var imageBytes = await Task.Run(() => CreateGameImage(capturedBytes, frame, metrics, format));

			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			await File.WriteAllBytesAsync(path, imageBytes);
		}

		private static async Task<FrameBounds> FindGameFrameAsync(CoreWebView2 core, ISet<CoreWebView2Frame> loadedFrames)
		{
			// Use main-document coordinates; nested-frame coordinates are composed below.
			const string gameFrameScript = """
				(() => {
					const frames = Array.from(document.querySelectorAll('iframe')).filter(item =>
						new URL(item.src || 'about:blank', document.baseURI).pathname === '/kcs2/index.php');
					if (frames.length !== 1) return null;
					const frame = frames[0];
					const rect = frame.getBoundingClientRect();
					if (rect.width <= 0 || rect.height <= 0) return null;
					return {
						x: rect.x + (window === window.top ? window.scrollX : 0),
						y: rect.y + (window === window.top ? window.scrollY : 0),
						width: rect.width,
						height: rect.height,
						gameUrl: frame.src,
						parentUrl: location.origin + location.pathname
					};
				})()
				""";

			var directResult = await core.ExecuteScriptAsync(gameFrameScript);
			var directFrame = JsonSerializer.Deserialize<FrameBounds?>(directResult, jsonOptions);
			if (directFrame is not null && WebView2HttpCapture.IsGamePageUrl(directFrame.GameUrl)) return directFrame;

			const string containerScript = """
				(() => {
					const frames = Array.from(document.querySelectorAll('iframe#game_frame'));
					if (frames.length !== 1) return null;
					const frame = frames[0];
					const rect = frame.getBoundingClientRect();
					if (rect.width <= 0 || rect.height <= 0 || frame.offsetWidth <= 0 || frame.offsetHeight <= 0) return null;
					const scaleX = rect.width / frame.offsetWidth;
					const scaleY = rect.height / frame.offsetHeight;
					const url = new URL(frame.src, document.baseURI);
					return {
						x: rect.x + window.scrollX + frame.clientLeft * scaleX,
						y: rect.y + window.scrollY + frame.clientTop * scaleY,
						scaleX,
						scaleY,
						url: url.origin + url.pathname
					};
				})()
				""";
			var containerResult = await core.ExecuteScriptAsync(containerScript);
			var container = JsonSerializer.Deserialize<FrameContainer?>(containerResult, jsonOptions)
				?? throw new InvalidOperationException("外側のgame_frameが見つかりません。ページ全体は代替撮影しません。");

			var matches = new List<FrameBounds>();
			foreach (var frame in loadedFrames.ToArray())
			{
				if (!loadedFrames.Contains(frame) || frame.IsDestroyed() != 0) continue;
				var innerResult = await frame.ExecuteScriptAsync(gameFrameScript);
				var inner = JsonSerializer.Deserialize<FrameBounds?>(innerResult, jsonOptions);
				if (inner is null
					|| !string.Equals(inner.ParentUrl, container.Url, StringComparison.Ordinal)
					|| !WebView2HttpCapture.IsGamePageUrl(inner.GameUrl))
				{
					continue;
				}

				matches.Add(new FrameBounds(
					container.X + inner.X * container.ScaleX,
					container.Y + inner.Y * container.ScaleY,
					inner.Width * container.ScaleX,
					inner.Height * container.ScaleY));
			}

			return matches.Count == 1
				? matches[0]
				: throw new InvalidOperationException("game_frame内の正規ゲームiframeを一意に特定できません。読み込み完了後に再試行してください。ページ全体は代替撮影しません。");
		}

		private static byte[] CreateGameImage(byte[] capturedBytes, FrameBounds frame, CaptureMetrics metrics, SupportedImageFormat format)
		{
			if (!double.IsFinite(frame.X) || !double.IsFinite(frame.Y)
				|| !double.IsFinite(frame.Width) || !double.IsFinite(frame.Height)
				|| frame.Width <= 0 || frame.Height <= 0)
			{
				throw new InvalidOperationException("ゲームiframeの表示領域を取得できませんでした。");
			}
			if (!double.IsFinite(metrics.PixelRatio) || metrics.PixelRatio <= 0
				|| !double.IsFinite(metrics.ScrollX) || !double.IsFinite(metrics.ScrollY))
				throw new InvalidOperationException("撮影用の表示情報が不正です。");

			using var input = new MemoryStream(capturedBytes, writable: false);
			var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
			if (decoder.Frames.Count == 0) throw new InvalidOperationException("画像データをデコードできませんでした。");
			var source = decoder.Frames[0];
			var targetRatio = (double)TargetScreenshotWidth / TargetScreenshotHeight;
			var width = Math.Min(frame.Width, frame.Height * targetRatio);
			var height = width / targetRatio;
			var left = Math.Round((frame.X - metrics.ScrollX + (frame.Width - width) / 2) * metrics.PixelRatio);
			var top = Math.Round((frame.Y - metrics.ScrollY + (frame.Height - height) / 2) * metrics.PixelRatio);
			var right = Math.Round((frame.X - metrics.ScrollX + (frame.Width + width) / 2) * metrics.PixelRatio);
			var bottom = Math.Round((frame.Y - metrics.ScrollY + (frame.Height + height) / 2) * metrics.PixelRatio);
			if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom)
				|| left < 0 || top < 0 || right > source.PixelWidth || bottom > source.PixelHeight
				|| right <= left || bottom <= top)
			{
				throw new InvalidOperationException("ゲーム領域全体が撮影範囲に収まっていません。ゲーム画面全体を表示してから再試行してください。");
			}

			var cropped = new CroppedBitmap(source, new Int32Rect((int)left, (int)top, (int)(right - left), (int)(bottom - top)));
			var resized = new TransformedBitmap(cropped, new ScaleTransform(
				(double)TargetScreenshotWidth / cropped.PixelWidth, (double)TargetScreenshotHeight / cropped.PixelHeight));
			if (resized.PixelWidth != TargetScreenshotWidth || resized.PixelHeight != TargetScreenshotHeight)
				throw new InvalidOperationException($"撮影寸法が要件と異なります: {resized.PixelWidth}×{resized.PixelHeight} px (期待値 1200×720 px)");

			BitmapEncoder encoder = format == SupportedImageFormat.Jpeg
				? new JpegBitmapEncoder { QualityLevel = 90 }
				: new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(resized));
			using var output = new MemoryStream();
			encoder.Save(output);
			return output.ToArray();
		}

		private sealed record FrameBounds(
			double X,
			double Y,
			double Width,
			double Height,
			string? GameUrl = null,
			string? ParentUrl = null);

		private sealed record FrameContainer(double X, double Y, double ScaleX, double ScaleY, string Url);
		private sealed record CaptureMetrics(double PixelRatio, double ScrollX, double ScrollY);
	}
}
