using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Microsoft.Web.WebView2.Core;

namespace Grabacr07.KanColleViewer.Models.WebView2
{
	internal static class WebView2HttpCapture
	{
		internal const int MaxCapturedBodyBytes = 5 * 1024 * 1024;

		internal static bool TryGetGameApiUri(string? url, out Uri uri)
		{
			uri = null!;
			if (!KanColleServerOrigin.IsValid(url)
				|| !Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
			{
				return false;
			}

			// ゲーム本体ページ（/kcs2/index.php）は API 解析に使わないため対象外です。
			// WebView2 では本文を取得できない場合もあります。
			var path = parsedUri.AbsolutePath;
			if (path.IndexOf("kcsapi", StringComparison.OrdinalIgnoreCase) < 0
				&& path.IndexOf("/api/", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}

			uri = parsedUri;
			return true;
		}

		internal static bool IsGamePageUrl(string? url)
		{
			return KanColleServerOrigin.IsValid(url)
				&& Uri.TryCreate(url, UriKind.Absolute, out var uri)
				&& string.Equals(uri.AbsolutePath, "/kcs2/index.php", StringComparison.OrdinalIgnoreCase);
		}

		internal static bool IsMaintenanceImage(string? url)
		{
			return KanColleServerOrigin.IsValid(url)
				&& Uri.TryCreate(url, UriKind.Absolute, out var uri)
				&& uri.AbsolutePath.IndexOf("maintenance.png", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		internal static IDictionary<string, string> SnapshotResponseHeaders(CoreWebView2HttpResponseHeaders responseHeaders)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var name in new[] { "Content-Encoding", "Content-Type" })
			{
				if (responseHeaders.Contains(name)) result[name] = responseHeaders.GetHeader(name);
			}
			return result;
		}

		internal static BodyCapture CaptureRequestBody(Stream? stream, CancellationToken cancellationToken)
		{
			if (stream is null) return new BodyCapture(Array.Empty<byte>(), false, false, null);

			try
			{
				if (!stream.CanRead) return new BodyCapture(null, false, true, "StreamNotReadable");

				var canRestorePosition = stream.CanSeek;
				var originalPosition = canRestorePosition ? stream.Position : 0;
				if (canRestorePosition) stream.Position = 0;

				try
				{
					return ReadBoundedBody(stream, cancellationToken);
				}
				finally
				{
					if (canRestorePosition) stream.Position = originalPosition;
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				return new BodyCapture(null, false, true, ex.GetType().Name);
			}
		}

		internal static BodyCapture ReadBoundedBody(Stream stream, CancellationToken cancellationToken)
		{
			using var body = new MemoryStream();
			var buffer = new byte[16 * 1024];

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var remaining = MaxCapturedBodyBytes + 1 - (int)body.Length;
				if (remaining <= 0) return new BodyCapture(null, true, true, null);

				var bytesRead = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
				if (bytesRead == 0) return new BodyCapture(body.ToArray(), false, true, null);
				if (bytesRead > MaxCapturedBodyBytes - body.Length)
					return new BodyCapture(null, true, true, null);

				body.Write(buffer, 0, bytesRead);
			}
		}

		internal static async Task<BodyCapture> ReadBoundedBodyAsync(Stream stream, CancellationToken cancellationToken)
		{
			using var body = new MemoryStream();
			var buffer = new byte[16 * 1024];

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var remaining = MaxCapturedBodyBytes + 1 - (int)body.Length;
				if (remaining <= 0) return new BodyCapture(null, true, true, null);

				var bytesRead = await stream.ReadAsync(
					buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
				if (bytesRead == 0) return new BodyCapture(body.ToArray(), false, true, null);
				if (bytesRead > MaxCapturedBodyBytes - body.Length)
					return new BodyCapture(null, true, true, null);

				body.Write(buffer, 0, bytesRead);
			}
		}

		internal static string DecodeRequestBody(byte[] bytes) => DecodeText(bytes, Encoding.UTF8);

		internal static string DecodeResponseBody(byte[] bytes, IDictionary<string, string> headers)
		{
			var contentEncoding = headers.TryGetValue("Content-Encoding", out var encoding)
				? encoding
				: string.Empty;
			var decodedBytes = DecodeContentEncoding(bytes, contentEncoding);
			var contentType = headers.TryGetValue("Content-Type", out var type) ? type : string.Empty;
			return DecodeText(decodedBytes, GetEncoding(contentType));
		}

		private static byte[] DecodeContentEncoding(byte[] bytes, string contentEncoding)
		{
			var encodings = contentEncoding
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Reverse()
				.ToArray();

			if (encodings.Length == 0 && IsGzip(bytes))
				return Decompress(bytes, input => new GZipStream(input, CompressionMode.Decompress));

			foreach (var encoding in encodings)
			{
				if (IsLikelyText(bytes)) break;

				switch (encoding.ToLowerInvariant())
				{
					case "gzip":
						if (!IsGzip(bytes))
							throw new InvalidDataException("gzip response did not contain a gzip stream.");
						bytes = Decompress(bytes, input => new GZipStream(input, CompressionMode.Decompress));
						break;
					case "deflate":
						try
						{
							bytes = Decompress(bytes, input => new ZLibStream(input, CompressionMode.Decompress));
						}
						catch (InvalidDataException)
						{
							bytes = Decompress(bytes, input => new DeflateStream(input, CompressionMode.Decompress));
						}
						break;
					case "br":
						bytes = Decompress(bytes, input => new BrotliStream(input, CompressionMode.Decompress));
						break;
					case "identity":
						break;
					default:
						throw new InvalidDataException($"Unsupported content encoding: {encoding}.");
				}
			}

			return bytes;
		}

		private static byte[] Decompress(byte[] bytes, Func<Stream, Stream> createDecoder)
		{
			using var input = new MemoryStream(bytes, writable: false);
			using var decoder = createDecoder(input);
			using var output = new MemoryStream();
			var buffer = new byte[16 * 1024];

			int bytesRead;
			while ((bytesRead = decoder.Read(buffer, 0, buffer.Length)) > 0)
			{
				if (bytesRead > MaxCapturedBodyBytes - output.Length)
					throw new InvalidDataException("Decompressed response exceeded the 5 MiB limit.");
				output.Write(buffer, 0, bytesRead);
			}

			return output.ToArray();
		}

		private static bool IsGzip(byte[] bytes) => bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

		private static bool IsLikelyText(byte[] bytes)
		{
			if (bytes.Length == 0) return true;
			var sampleLength = Math.Min(bytes.Length, 512);
			var textBytes = 0;
			for (var index = 0; index < sampleLength; index++)
			{
				var value = bytes[index];
				if (value >= 0x20 || value == '\t' || value == '\r' || value == '\n') textBytes++;
			}
			return textBytes >= sampleLength * 0.9;
		}

		private static Encoding GetEncoding(string contentType)
		{
			foreach (var parameter in contentType.Split(';').Skip(1))
			{
				var separator = parameter.IndexOf('=');
				if (separator < 0 || !string.Equals(parameter[..separator].Trim(), "charset", StringComparison.OrdinalIgnoreCase))
					continue;

				var charset = parameter[(separator + 1)..].Trim().Trim('"', '\'');
				try
				{
					return Encoding.GetEncoding(charset);
				}
				catch (ArgumentException)
				{
					return Encoding.UTF8;
				}
				catch (NotSupportedException)
				{
					return Encoding.UTF8;
				}
			}

			return Encoding.UTF8;
		}

		private static string DecodeText(byte[] bytes, Encoding encoding)
		{
			var text = encoding.GetString(bytes);
			return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
		}

		internal readonly struct BodyCapture
		{
			internal BodyCapture(byte[]? bytes, bool isTruncated, bool hasStream, string? error)
			{
				this.Bytes = bytes;
				this.IsTruncated = isTruncated;
				this.HasStream = hasStream;
				this.Error = error;
			}

			internal byte[]? Bytes { get; }
			internal bool IsTruncated { get; }
			internal bool HasStream { get; }
			internal string? Error { get; }
		}
	}
}
