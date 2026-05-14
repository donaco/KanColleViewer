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
	// RequestHandler / ResourceRequestHandler 実装
	public class CustomRequestHandler : RequestHandler
	{
		private readonly Action<CapturedHttp> onCaptured;

		public CustomRequestHandler(Action<CapturedHttp> onCaptured)
		{
			this.onCaptured = onCaptured;
		}

		protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
		{
			// メンテナンス時、埋め込みブラウザで画像だけ表示
			try
			{
				var url = request?.Url;
				if (!string.IsNullOrEmpty(url) && url.IndexOf("maintenance.png", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					// スキームを http / https に限定（file://, javascript: 等を排除）
					if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
						&& (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
					{
						// ChromiumWebBrowser インスタンスにキャストできれば UI スレッドでナビゲート
						try
						{
							var cb = chromiumWebBrowser as CefSharp.Wpf.ChromiumWebBrowser;
							if (cb != null)
							{
								cb.Dispatcher.BeginInvoke(new Action(() =>
								{
									try
									{
										// 繰り返しナビゲートを避けるため簡易フラグを Tag に記録
										const string flag = "maintenance_shown";
										if (cb.Tag as string != flag)
										{
											cb.Tag = flag;
											cb.Load(url);
										}
									}
									catch
									{
										// swallow
									}
								}));
							}
						}
						catch
						{
							// swallow
						}
					}
				}
			}
			catch
			{
				// swallow
			}

			return new CustomResourceRequestHandler(onCaptured);
		}
	}

	public class CustomResourceRequestHandler : ResourceRequestHandler
	{
		private readonly Action<CapturedHttp> onCaptured;

		public CustomResourceRequestHandler(Action<CapturedHttp> onCaptured)
		{
			this.onCaptured = onCaptured;
		}

		protected override IResponseFilter GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
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
				// 受け取った bytes をそのまま Task に渡して非同期で処理する
				try
				{
					var copy = bytes != null ? (byte[])bytes.Clone() : new byte[0];
					Task.Run(() =>
					{
						string responseBodyText = null;
						try { responseBodyText = ResponseFilter.TryDecode(copy); } catch { responseBodyText = null; }

						// gzip 判定: ヘッダー × バイト先頭マジックを組み合わせつつ、
						// バイナリ内に gzip マジックが埋まっている場合はそのオフセットから展開を試す
						string normalized = null;
						try
						{
							// バイト列内で gzip マジック (0x1F 0x8B) を探す
							int gzipOffset = -1;
							if (copy != null && copy.Length >= 2)
							{
								for (int i = 0; i < copy.Length - 1; i++)
								{
									if (copy[i] == 0x1F && copy[i + 1] == 0x8B) { gzipOffset = i; break; }
								}
							}

							if (gzipOffset >= 0)
							{
								// 見つかったオフセットから展開を試みる
								try
								{
									using (var ms = new MemoryStream(copy, gzipOffset, copy.Length - gzipOffset))
									using (var gz = new GZipStream(ms, CompressionMode.Decompress))
									using (var sr = new StreamReader(gz, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
									{
										var decompressed = sr.ReadToEnd();
										normalized = Grabacr07.KanColleWrapper.Internal.RetryObservableExtensions.NormalizeSvDataString(decompressed);
									}
								}
								catch
								{
									normalized = null;
								}
							}

							// フォールバック: 文字列化済みテキストから正規化を試す（すでに展開済み／非圧縮のケース）
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
								RequestBody = snapshotRequestBody,
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

		private static IDictionary<string, string> BuildHeadersDictionary(IResponse response)
		{
			if (response?.Headers == null) return null;
			var dict = new Dictionary<string, string>();
			foreach (var key in response.Headers.AllKeys)
			{
				dict[key] = response.Headers[key];
			}
			return dict;
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
						// ゲーム API の POST は Bytes のみ。File タイプは艦これでは使用しないため無視する。
						if (element.Type == PostDataElementType.Bytes)
						{
							var bytes = element.Bytes;
							if (bytes != null && bytes.Length > 0) bytesList.AddRange(bytes);
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
