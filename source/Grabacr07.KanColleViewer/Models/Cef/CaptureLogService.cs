using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	public sealed class CaptureLogService
	{
		private static readonly Lazy<CaptureLogService> lazy = new Lazy<CaptureLogService>(() => new CaptureLogService());
		public static CaptureLogService Instance => lazy.Value;

		// UI バインド用コレクション（UI スレッドで操作する）
		public ObservableCollection<CapturedHttp> Entries { get; } = new ObservableCollection<CapturedHttp>();

		// 最大保持件数（必要に応じて増やす）
		public int MaxEntries { get; set; } = 1000;

		private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

		private CaptureLogService() { }

		public void Add(CapturedHttp entry)
		{
			if (entry == null) return;

			// UI スレッドにディスパッチして追加
			if (!dispatcher.CheckAccess())
			{
				dispatcher.BeginInvoke(new Action(() => AddInternal(entry)));
			}
			else
			{
				AddInternal(entry);
			}
		}

		private void AddInternal(CapturedHttp entry)
		{
			try
			{
				Entries.Insert(0, entry); // 新しいものを先頭に
				// 上限を超えたら末尾削除
				while (Entries.Count > MaxEntries)
				{
					Entries.RemoveAt(Entries.Count - 1);
				}
			}
			catch
			{
				// ログ追加失敗は無視（保守用）
			}
		}

		public void Clear()
		{
			if (!dispatcher.CheckAccess())
			{
				dispatcher.BeginInvoke(new Action(() => Entries.Clear()));
			}
			else
			{
				Entries.Clear();
			}
		}
	}
}
