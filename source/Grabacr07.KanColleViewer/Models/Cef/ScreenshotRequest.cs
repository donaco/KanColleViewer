using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	public class ScreenshotRequest
	{
		private readonly TaskCompletionSource<Unit> source;
		private readonly string path;

		public string Id { get; }

		public ScreenshotRequest(string path, TaskCompletionSource<Unit> source)
		{
			this.Id = $"ssReq{DateTimeOffset.Now.Ticks}";
			this.path = path;
			this.source = source;
		}

		public void Complete(string dataUrl)
		{
			try
			{
				// エラーレスポンスの確認
				if (dataUrl.StartsWith("error:"))
				{
					throw new Exception($"スクリーンショット取得エラー: {dataUrl.Substring(6)}");
				}

				if (string.IsNullOrEmpty(dataUrl))
				{
					throw new Exception("無効な形式: dataUrl が空です");
				}

				var array = dataUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length != 2)
				{
					throw new Exception($"無効な形式: カンマで区切られた2つの要素が必要です (実際: {array.Length}個)");
				}

				var base64 = array[1];
				var bytes = Convert.FromBase64String(base64);

				using (var fs = new FileStream(this.path, FileMode.CreateNew))
				{
					fs.Write(bytes, 0, bytes.Length);
				}

				this.source.SetResult(Unit.Default);
			}
			catch (Exception ex)
			{
				this.source.SetException(ex);
			}
		}
	}
}
