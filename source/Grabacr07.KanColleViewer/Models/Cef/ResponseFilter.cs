using System;
using System.IO;
using System.Text;
using CefSharp;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	// レスポンス本文をバッファして完了時にコールバックする IResponseFilter 実装
	public class ResponseFilter : IResponseFilter
	{
		private readonly MemoryStream buffer = new MemoryStream();
		private readonly Action<byte[]> onCompleted;

		public ResponseFilter(Action<byte[]> onCompleted)
		{
			this.onCompleted = onCompleted;
		}

		public bool InitFilter() => true;

		// dataIn == null のタイミングで完了（CEF の仕様）
		public FilterStatus Filter(Stream dataIn, out long dataInRead, Stream dataOut, out long dataOutWritten)
		{
			dataInRead = 0;
			dataOutWritten = 0;

			if (dataIn == null)
			{
				try { onCompleted?.Invoke(buffer.ToArray()); } catch { }
				return FilterStatus.Done;
			}

			var readBuffer = new byte[8192];
			int read;
			while ((read = dataIn.Read(readBuffer, 0, readBuffer.Length)) > 0)
			{
				buffer.Write(readBuffer, 0, read);
				dataInRead += read;

				// パススルーしてブラウザへ返す
				dataOut.Write(readBuffer, 0, read);
				dataOutWritten += read;
			}

			return FilterStatus.NeedMoreData;
		}

		public void Dispose() => buffer?.Dispose();

		// ヘルパー: バイト配列を可能な限り文字列化（UTF-8 → default → Base64）
		public static string TryDecode(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0) return null;
			try { return Encoding.UTF8.GetString(bytes); }
			catch
			{
				try { return Encoding.Default.GetString(bytes); }
				catch { return Convert.ToBase64String(bytes); }
			}
		}
	}
}
