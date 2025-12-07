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

			// dataOut が一度に受け取れる残り容量を計算する
			long remaining;
			// UnmanagedMemoryStream なら Capacity - Position を使う（CEF で渡されるケース）
			var ums = dataOut as UnmanagedMemoryStream;
			if (ums != null)
			{
				remaining = ums.Capacity - ums.Position;
			}
			else if (dataOut.CanSeek)
			{
				// 他の実装の場合は Length - Position を試す
				remaining = dataOut.Length - dataOut.Position;
			}
			else
			{
				// 追跡できない場合は大きな値を許可（ただし通常 CEF の dataOut は固定長）
				remaining = int.MaxValue;
			}

			if (remaining <= 0)
			{
				// 今回は書き込み先に空きが無いため、もっとデータが必要（または空き待ち）
				return FilterStatus.NeedMoreData;
			}

			// 残容量に収まる範囲で読み取り・書き込みを行う
			while (remaining > 0 && (read = dataIn.Read(readBuffer, 0, (int)Math.Min(readBuffer.Length, remaining))) > 0)
			{
				buffer.Write(readBuffer, 0, read);
				dataInRead += read;

				// パススルーしてブラウザへ返す（残容量を超えないよう制限済み）
				dataOut.Write(readBuffer, 0, read);
				dataOutWritten += read;

				remaining -= read;
			}

			// dataIn にまだデータが残っている可能性があるが、dataOut の空きが無ければ次回呼び出しを待つ
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
