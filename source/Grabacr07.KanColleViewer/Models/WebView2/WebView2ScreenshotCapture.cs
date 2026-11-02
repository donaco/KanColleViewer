using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

			var captureFormat = format switch
			{
				SupportedImageFormat.Png => "png",
				SupportedImageFormat.Jpeg => "jpeg",
				_ => throw new ArgumentOutOfRangeException(nameof(format), format, "PNGまたはJPEGを指定してください。"),
			};
			var styleId = JsonSerializer.Serialize($"webview2-screenshot-{Guid.NewGuid():N}");
			byte[] imageBytes;
			(int Width, int Height) dimensions;

			try
			{
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
					throw new InvalidOperationException("撮影用CSSを適用できませんでした。");

				var clip = CreateScreenshotClip(await FindGameFrameAsync(core, loadedFrames));
				var screenshotParameters = new Dictionary<string, object>
				{
					["format"] = captureFormat,
					["clip"] = new { x = clip.X, y = clip.Y, width = clip.Width, height = clip.Height, scale = clip.Scale },
					["fromSurface"] = true,
					["captureBeyondViewport"] = true,
				};
				if (format == SupportedImageFormat.Jpeg) screenshotParameters["quality"] = 90;

				await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}");
				var responseJson = await core.CallDevToolsProtocolMethodAsync(
					"Page.captureScreenshot",
					JsonSerializer.Serialize(screenshotParameters));
				var response = JsonSerializer.Deserialize<ScreenshotResponse>(responseJson, jsonOptions)
					?? throw new InvalidOperationException("DevTools Protocolから撮影データが返りませんでした。");
				if (string.IsNullOrEmpty(response.Data))
					throw new InvalidOperationException("撮影データが空です。");

				imageBytes = Convert.FromBase64String(response.Data);
				dimensions = ReadImageDimensions(imageBytes);
				if (dimensions.Width != TargetScreenshotWidth || dimensions.Height != TargetScreenshotHeight)
					throw new InvalidOperationException($"撮影寸法が要件と異なります: {dimensions.Width}×{dimensions.Height} px (期待値 1200×720 px)");
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
					throw new InvalidOperationException("撮影用CSSを解除できませんでした。ページを更新してください。");
			}

			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			await File.WriteAllBytesAsync(path, imageBytes);
		}

		private static async Task<FrameBounds> FindGameFrameAsync(CoreWebView2 core, ISet<CoreWebView2Frame> loadedFrames)
		{
			// CDP uses main-document coordinates; nested-frame coordinates are composed below.
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

		private static ScreenshotClip CreateScreenshotClip(FrameBounds frame)
		{
			if (!double.IsFinite(frame.X) || !double.IsFinite(frame.Y)
				|| !double.IsFinite(frame.Width) || !double.IsFinite(frame.Height)
				|| frame.Width <= 0 || frame.Height <= 0)
			{
				throw new InvalidOperationException("ゲームiframeの表示領域を取得できませんでした。");
			}

			var targetRatio = (double)TargetScreenshotWidth / TargetScreenshotHeight;
			if (frame.Width / frame.Height > targetRatio)
			{
				var croppedWidth = frame.Height * targetRatio;
				return new ScreenshotClip(
					frame.X + (frame.Width - croppedWidth) / 2,
					frame.Y,
					croppedWidth,
					frame.Height,
					TargetScreenshotWidth / croppedWidth);
			}

			var croppedHeight = frame.Width / targetRatio;
			return new ScreenshotClip(
					frame.X,
					frame.Y + (frame.Height - croppedHeight) / 2,
					frame.Width,
					croppedHeight,
					TargetScreenshotWidth / frame.Width);
		}

		private static (int Width, int Height) ReadImageDimensions(byte[] imageBytes)
		{
			using var stream = new MemoryStream(imageBytes, writable: false);
			var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
			if (decoder.Frames.Count == 0) throw new InvalidOperationException("画像データをデコードできませんでした。");
			var frame = decoder.Frames[0];
			return (frame.PixelWidth, frame.PixelHeight);
		}

		private sealed record FrameBounds(
			double X,
			double Y,
			double Width,
			double Height,
			string? GameUrl = null,
			string? ParentUrl = null);

		private sealed record FrameContainer(double X, double Y, double ScaleX, double ScaleY, string Url);
		private sealed record ScreenshotClip(double X, double Y, double Width, double Height, double Scale);
		private sealed record ScreenshotResponse(string? Data);
	}
}
