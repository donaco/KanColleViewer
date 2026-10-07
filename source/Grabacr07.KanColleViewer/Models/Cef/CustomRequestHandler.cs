using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Handler;
using Grabacr07.KanColleViewer.Models.Settings;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	// RequestHandler / ResourceRequestHandler 実装
	public class CustomRequestHandler : RequestHandler
	{
		private readonly Action<CapturedHttp> onCaptured;

		public CustomRequestHandler(Action<CapturedHttp> onCaptured)
		{
			this.onCaptured = onCaptured;
		}

		protected override bool OnCertificateError(IWebBrowser chromiumWebBrowser, IBrowser browser, CefErrorCode errorCode, string requestUrl, ISslInfo sslInfo, IRequestCallback callback)
		{
			var allow = false;

			try
			{
				allow = CefBridge.IsRelayProxyRunning
					&& NetworkSettings.Relay.IsEnabled.Value
					&& Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri)
					&& uri.Scheme == Uri.UriSchemeHttps
					&& Grabacr07.KanColleWrapper.KanColleServerOrigin.IsAllowedHost(uri.Host);
			}
			catch
			{
				// 判定できない場合は拒否します。
			}

			callback.Continue(allow);
			return true;
		}

		/// <summary>
		/// DevToolsのNetworkタブでダブルクリックした際の
		/// kcsapi URLへのメインフレームナビゲーションをブロックする
		/// </summary>
		protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
		{
			try
			{
				if (frame?.IsMain == true)
				{
					var url = request?.Url ?? string.Empty;

					// kcsapi URL はゲーム内 API のレスポンスであり、
					// メインフレームのナビゲーション先としては不正（DevToolsからのWクリック）
					if (url.IndexOf("kcsapi", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						System.Diagnostics.Debug.WriteLine($"[DevTools] blocked navigation to: {url}");
						return true; // ナビゲーションをキャンセル
					}
				}
			}
			catch
			{
				// swallow
			}

			return false; // 通常のナビゲーションを許可
		}

		protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
		{
			// メンテナンス時、埋め込みブラウザで画像だけ表示
			try
			{
				var url = request?.Url;
				if (!string.IsNullOrEmpty(url) && url.IndexOf("maintenance.png", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
						&& (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
					{
						try
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
						catch { }
					}
				}
			}
			catch { }

			return new CustomResourceRequestHandler(onCaptured, frame?.IsMain ?? false);
		}

		private static bool IsDevToolsNavigation(string url)
		{
			if (string.IsNullOrEmpty(url)) return false;
			return url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
			       url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase);
		}
	}

	public class CustomResourceRequestHandler : ResourceRequestHandler
	{
		private readonly Action<CapturedHttp> onCaptured;
		private readonly bool isMainFrame;

		public CustomResourceRequestHandler(Action<CapturedHttp> onCaptured, bool isMainFrame)
		{
			this.onCaptured = onCaptured;
			this.isMainFrame = isMainFrame;
		}

		protected override IResponseFilter? GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
		{
			if (request?.Url == null) return null;

			// ① オリジン検証：艦これ正規サーバー以外からのレスポンスは処理しない
			if (!Grabacr07.KanColleWrapper.KanColleServerOrigin.IsValid(request.Url))
				return null;

			// ② パス検証：kcsapi 等のゲーム API エンドポイントのみ対象とする（既存ロジック）
			if (!(request.Url.Contains("kcsapi") || request.Url.Contains("/api/") || request.Url.Contains("/kcs2/index.php")))
			{
				return null;
			}

			var snapshotUrl = request.Url;
			var snapshotMethod = request.Method;
			var snapshotStatus = response?.StatusCode;
			var snapshotRequestBody = ExtractRequestBody(request);
			var snapshotResponseHeaders = BuildHeadersDictionary(response);

			// ResponseFilter のコールバックは短くして、重い処理は Task.Run にオフロードする
			return new ResponseFilter(bytes =>
			{
				if (bytes == null || bytes.Length == 0 || bytes.Length > ResponseFilter.MaxCapturedBodyBytes)
					return;

				// 受け取った bytes をそのまま Task に渡して非同期で処理する
				try
				{
					var copy = (byte[])bytes.Clone();
					Task.Run(() =>
					{
						string? responseBodyText = null;
						try { responseBodyText = ResponseFilter.TryDecode(copy); } catch { responseBodyText = null; }

						string? normalized = null;
						try
						{
							if (ShouldDecompressGzip(snapshotResponseHeaders, copy))
							{
								var decompressed = TryDecompressGzip(copy);
								if (!string.IsNullOrEmpty(decompressed))
								{
									normalized = Grabacr07.KanColleWrapper.Internal.RetryObservableExtensions.NormalizeSvDataString(decompressed);
								}
							}

							if (string.IsNullOrEmpty(normalized))
							{
								normalized = Grabacr07.KanColleWrapper.Internal.RetryObservableExtensions.NormalizeSvDataString(responseBodyText ?? string.Empty);
							}
						}
						catch
						{
							normalized = null;
						}

						// 正常に正規化できたらアプリへ渡す（onCaptured は別スレッドで安全に呼ぶ)
						if (!string.IsNullOrEmpty(normalized))
						{
							var captured = new CapturedHttp
							{
								Url = snapshotUrl,
								Method = snapshotMethod,
								StatusCode = snapshotStatus ?? 0,
								RequestBody = snapshotRequestBody ?? string.Empty,
								ResponseBody = normalized,
								ResponseHeaders = snapshotResponseHeaders
							};

							try
							{
								// onCaptured は軽量にする想定だが念のためも別スレッドで
								Task.Run(() => { try { onCaptured?.Invoke(captured); } catch { } });
							}
							catch { }
						}
					});
				}
				catch
				{
					// swallow
				}
			});
		}

		private static IDictionary<string, string> BuildHeadersDictionary(IResponse? response)
		{
			var dict = new Dictionary<string, string>();
			if (response?.Headers == null) return dict;
			foreach (var key in response.Headers.AllKeys)
			{
				if (key != null)
					dict[key] = response.Headers[key] ?? string.Empty;
			}
			return dict;
		}

		private static bool ShouldDecompressGzip(IDictionary<string, string> headers, byte[] bytes)
		{
			if (bytes == null || bytes.Length < 2 || bytes.Length > ResponseFilter.MaxCapturedBodyBytes)
			{
				return false;
			}

			if (bytes[0] != 0x1F || bytes[1] != 0x8B)
			{
				return false;
			}

			if (headers == null)
			{
				return false;
			}

			if (!headers.TryGetValue("Content-Encoding", out var contentEncoding))
			{
				var header = headers.FirstOrDefault(x => string.Equals(x.Key, "Content-Encoding", StringComparison.OrdinalIgnoreCase));
				contentEncoding = header.Value;
			}

			return !string.IsNullOrWhiteSpace(contentEncoding)
				&& contentEncoding.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string? TryDecompressGzip(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0 || bytes.Length > ResponseFilter.MaxCapturedBodyBytes)
			{
				return null;
			}

			try
			{
				using (var ms = new MemoryStream(bytes, writable: false))
				using (var gz = new GZipStream(ms, CompressionMode.Decompress))
			using (var decompressed = new MemoryStream())
				{
					var readBuffer = new byte[8192];
					int read;
					while ((read = gz.Read(readBuffer, 0, readBuffer.Length)) > 0)
					{
						if (read > ResponseFilter.MaxCapturedBodyBytes - decompressed.Length)
							return null;

						decompressed.Write(readBuffer, 0, read);
					}

					return Encoding.UTF8.GetString(decompressed.GetBuffer(), 0, (int)decompressed.Length);
				}
			}
			catch
			{
				return null;
			}
		}

		private static string? ExtractRequestBody(IRequest request)
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
						// ゲーム API の POST は Bytes のみ。File タイプは艦これでは使用しないため無視する。
						if (element.Type == PostDataElementType.Bytes)
						{
							var bytes = element.Bytes;
							if (bytes != null && bytes.Length > 0)
							{
								if (bytes.Length > ResponseFilter.MaxCapturedBodyBytes - bytesList.Count)
									return null;

								bytesList.AddRange(bytes);
							}
						}
					}
					catch
					{
						// swallow
					}
				}
				return ResponseFilter.TryDecode(bytesList.ToArray());
			}
			catch
			{
				return null;
			}
		}
	}
}
