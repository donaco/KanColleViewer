using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Handler;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	public class CustomRequestHandler : RequestHandler
	{
		private readonly Action<CapturedHttp> onCaptured;

		public CustomRequestHandler(Action<CapturedHttp> onCaptured)
		{
			this.onCaptured = onCaptured;
		}

		// 購入フロー調査中は kcsapi ナビゲーションブロックを無効化
		// （DevTools誤操作対策より、正規フロー阻害回避を優先）
		protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
		{
			return false;
		}

		protected override IResourceRequestHandler GetResourceRequestHandler(
			IWebBrowser chromiumWebBrowser,
			IBrowser browser,
			IFrame frame,
			IRequest request,
			bool isNavigation,
			bool isDownload,
			string requestInitiator,
			ref bool disableDefaultHandling)
		{
			try
			{
				var url = request?.Url;
				if (!string.IsNullOrEmpty(url) && url.IndexOf("maintenance.png", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
						&& (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
					{
						var cb = chromiumWebBrowser as CefSharp.Wpf.ChromiumWebBrowser;
						if (cb != null)
						{
							cb.Dispatcher.BeginInvoke(new Action(() =>
							{
								try
								{
									const string flag = "maintenance_shown";
									if (cb.Tag as string != flag)
									{
										cb.Tag = flag;
										cb.Load(url);
									}
								}
								catch { }
							}));
						}
					}
				}

				// 艦これ API の応答だけをキャプチャする。
				if (!string.IsNullOrEmpty(url)
					&& Grabacr07.KanColleWrapper.KanColleServerOrigin.IsValid(url)
					&& CustomResourceRequestHandler.IsCapturableApiPath(url))
				{
					return new CustomResourceRequestHandler(onCaptured, frame?.IsMain ?? false);
				}
			}
			catch { }

			// DMM 購入画面、認証、CDN を含む非 API 通信は CEF 標準処理に委譲する。
			return null;
		}
	}

	public class DmmPopupLifeSpanHandler : ILifeSpanHandler
	{
		// popup を許可するのは「認証ページ」系のみ
		private static readonly string[] PopupDocumentHosts = new[]
		{
			"accounts.dmm.com",
			"www.dmm.com",
			"sp.dmm.com",
			"artemis.games.dmm.com",
			"dmm.com",
		};

		public bool OnBeforePopup(
			IWebBrowser chromiumWebBrowser,
			IBrowser browser,
			IFrame frame,
			string targetUrl,
			string targetFrameName,
			WindowOpenDisposition targetDisposition,
			bool userGesture,
			IPopupFeatures popupFeatures,
			IWindowInfo windowInfo,
			IBrowserSettings browserSettings,
			ref bool noJavascriptAccess,
			out IWebBrowser newBrowser)
		{
			newBrowser = null;

			if (string.IsNullOrWhiteSpace(targetUrl))
				return true;

			if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
				return true;

			// js/css/json 等のリソース URL を popup 許可対象にしない
			var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
			if (path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".map") || path.EndsWith(".json"))
			{
				System.Diagnostics.Debug.WriteLine($"[DmmPopup] blocked resource-like popup: {targetUrl}");
				return true;
			}

			var host = uri.Host.TrimStart('.').ToLowerInvariant();
			var allowedHost = PopupDocumentHosts.Any(h =>
				host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
				host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

			if (!allowedHost)
			{
				System.Diagnostics.Debug.WriteLine($"[DmmPopup] blocked non-auth popup host: {targetUrl}");
				return true;
			}

			// 認証フローに必要な経路のみ許可
			// （accounts の login/token 系 + DMM 支払い確認ページ）
			var lowerUrl = targetUrl.ToLowerInvariant();
			var looksLikeAuthDocument =
				lowerUrl.Contains("/service/login/token/") ||
				lowerUrl.Contains("/service/login/") ||
				lowerUrl.Contains("/payment/") ||
				lowerUrl.Contains("/purchase");

			if (!looksLikeAuthDocument)
			{
				System.Diagnostics.Debug.WriteLine($"[DmmPopup] blocked popup (not auth document): {targetUrl}");
				return true;
			}

			System.Diagnostics.Debug.WriteLine($"[DmmPopup] allowing popup: {targetUrl}");
			return false; // ネイティブ popup 許可
		}

		public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
		public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser) => false;
		public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
	}

	public class CustomResourceRequestHandler : ResourceRequestHandler
	{
		private const int MaxCapturedResponseBytes = 4 * 1024 * 1024;
		private readonly Action<CapturedHttp> onCaptured;
		private readonly bool isMainFrame;

		public CustomResourceRequestHandler(Action<CapturedHttp> onCaptured, bool isMainFrame)
		{
			this.onCaptured = onCaptured;
			this.isMainFrame = isMainFrame;
		}

		protected override IResponseFilter GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
		{
			if (request?.Url == null) return null;

			// ① オリジン検証：艦これ正規サーバー以外は処理しない
			if (!Grabacr07.KanColleWrapper.KanColleServerOrigin.IsValid(request.Url))
				return null;

			// ② パス検証：ゲーム API エンドポイントのみキャプチャ対象
			if (!IsCapturableApiPath(request.Url))
				return null;

			var snapshotUrl = request.Url;
			var snapshotMethod = request.Method;
			var snapshotStatus = response?.StatusCode;
			var snapshotRequestBody = ExtractRequestBody(request);
			var snapshotResponseHeaders = BuildHeadersDictionary(response);

			return new ResponseFilter(bytes =>
			{
				try
				{
					var copy = bytes != null ? (byte[])bytes.Clone() : new byte[0];
					Task.Run(() =>
					{
						string responseBodyText = null;
						try { responseBodyText = ResponseFilter.TryDecode(copy); } catch { responseBodyText = null; }

						string normalized = null;
						try
						{
							if (ShouldDecompressGzip(snapshotResponseHeaders, copy))
							{
								var decompressed = TryDecompressGzip(copy);
								if (!string.IsNullOrEmpty(decompressed))
									normalized = Grabacr07.KanColleWrapper.Internal.RetryObservableExtensions.NormalizeSvDataString(decompressed);
							}

							if (string.IsNullOrEmpty(normalized))
								normalized = Grabacr07.KanColleWrapper.Internal.RetryObservableExtensions.NormalizeSvDataString(responseBodyText ?? string.Empty);
						}
						catch { normalized = null; }

						if (!string.IsNullOrEmpty(normalized))
						{
							var captured = new CapturedHttp
							{
								Url = snapshotUrl,
								Method = snapshotMethod,
								StatusCode = snapshotStatus ?? 0,
								RequestBody = snapshotRequestBody,
								ResponseBody = normalized,
								ResponseHeaders = snapshotResponseHeaders
							};
							try { Task.Run(() => { try { onCaptured?.Invoke(captured); } catch { } }); } catch { }
						}
					});
				}
				catch { }
			});
		}

		internal static bool IsCapturableApiPath(string url)
		{
			return url.IndexOf("kcsapi", StringComparison.OrdinalIgnoreCase) >= 0
				|| url.IndexOf("/api/", StringComparison.OrdinalIgnoreCase) >= 0
				|| url.IndexOf("/kcs2/index.php", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static IDictionary<string, string> BuildHeadersDictionary(IResponse response)
		{
			if (response?.Headers == null) return null;
			var dict = new Dictionary<string, string>();
			foreach (var key in response.Headers.AllKeys)
				dict[key] = response.Headers[key];
			return dict;
		}

		private static bool ShouldDecompressGzip(IDictionary<string, string> headers, byte[] bytes)
		{
			if (bytes == null || bytes.Length < 2 || bytes.Length > MaxCapturedResponseBytes) return false;
			if (bytes[0] != 0x1F || bytes[1] != 0x8B) return false;
			if (headers == null) return false;

			string contentEncoding;
			if (!headers.TryGetValue("Content-Encoding", out contentEncoding))
			{
				var header = headers.FirstOrDefault(x => string.Equals(x.Key, "Content-Encoding", StringComparison.OrdinalIgnoreCase));
				contentEncoding = header.Value;
			}

			return !string.IsNullOrWhiteSpace(contentEncoding)
				&& contentEncoding.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string TryDecompressGzip(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0 || bytes.Length > MaxCapturedResponseBytes) return null;
			try
			{
				using (var ms = new MemoryStream(bytes, writable: false))
				using (var gz = new GZipStream(ms, CompressionMode.Decompress))
				using (var sr = new StreamReader(gz, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
					return sr.ReadToEnd();
			}
			catch { return null; }
		}

		private static string ExtractRequestBody(IRequest request)
		{
			try
			{
				var postData = request?.PostData;
				if (postData == null) return null;
				var elements = postData.Elements;
				if (elements == null || elements.Count == 0) return null;

				var bytesList = new List<byte>();
				foreach (var element in elements)
				{
					try
					{
						if (element.Type == PostDataElementType.Bytes)
						{
							var bytes = element.Bytes;
							if (bytes != null && bytes.Length > 0) bytesList.AddRange(bytes);
						}
					}
					catch { }
				}
				return ResponseFilter.TryDecode(bytesList.ToArray());
			}
			catch { return null; }
		}
	}
}
