using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

		// 修正: IResponse / IRequest のフィールドを即座にコピーしてコールバックで CefSharp オブジェクトに触らない
		protected override IResponseFilter GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
		{
			if (request?.Url == null) return null;

			// 簡易フィルタ（診断時は広めに拾う）
			if (!(request.Url.Contains("kcsapi") || request.Url.Contains("/api/") || request.Url.Contains("/kcs2/index.php")))
			{
				return null;
			}

			// -- スナップショット（ここで必要な情報をコピーしておく） --
			var snapshotUrl = request.Url;
			var snapshotMethod = request.Method;
			// response.StatusCode may not exist on all IResponse implementations; handle safely later
			var snapshotStatus = response?.StatusCode; // may be unavailable; diagnostic logging handles null
			var snapshotRequestBody = ExtractRequestBody(request); // IRequest をこの時点で扱う（同期）
			var snapshotResponseHeaders = BuildHeadersDictionary(response); // IResponse -> Dictionary にコピー

			// ResponseFilter のコールバック内では CefSharp 型にはアクセスしない
			return new ResponseFilter(bytes =>
			{
				try
				{
					var responseBody = ResponseFilter.TryDecode(bytes);

					var captured = new CapturedHttp
					{
						Url = snapshotUrl,
						Method = snapshotMethod,
						StatusCode = snapshotStatus ?? 0,
						RequestBody = snapshotRequestBody,
						ResponseBody = responseBody,
						ResponseHeaders = snapshotResponseHeaders
					};

					// 診断ログ: 重要な情報をローカルに残す（容量注意：短く）
					try
					{
						var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
						Directory.CreateDirectory(logDir);
						var logPath = Path.Combine(logDir, "cef_captured_diagnostic.log");

						var safeResp = responseBody ?? string.Empty;
						var preview = safeResp.Length > 4000 ? safeResp.Substring(0, 4000) + "..." : safeResp;

						var headerText = snapshotResponseHeaders != null
							? string.Join(", ", snapshotResponseHeaders.Select(kv => kv.Key + ":" + kv.Value))
							: "(no headers)";

						var entry = $"{DateTime.Now:O} URL={snapshotUrl}\nMethod={snapshotMethod} Status={snapshotStatus}\nHeaders={headerText}\nRequestBody={(snapshotRequestBody ?? "(none)").Replace("\r","").Replace("\n"," ")}\nResponsePreview:\n{preview}\n\n";
						File.AppendAllText(logPath, entry, Encoding.UTF8);
					}
					catch { /* swallow logging errors */ }

					try { onCaptured?.Invoke(captured); } catch { /* swallow */ }
				}
				catch (Exception ex)
				{
					// トラブルシュート用にログを残す（過度に多く残さないよう注意）
					try
					{
						var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs", "capture_errors.log");
						Directory.CreateDirectory(Path.GetDirectoryName(log));
						File.AppendAllText(log, $"{DateTime.Now}: ResponseFilter callback exception: {ex}\n");
					}
					catch { }
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
						if (element.Type == PostDataElementType.Bytes)
						{
							var bytes = element.Bytes;
							if (bytes != null && bytes.Length > 0) bytesList.AddRange(bytes);
						}
						else if (element.Type == PostDataElementType.File)
						{
							var filePath = element.File;
							if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
							{
								bytesList.AddRange(File.ReadAllBytes(filePath));
							}
						}
					}
					catch
					{
						// プロパティアクセスが失敗する可能性があるため吞む
					}
				}
				return ResponseFilter.TryDecode(bytesList.ToArray());
			}
			catch { return null; }
		}
	}
}
