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

		// 追加: 表示するホストのサフィックス（例: "kancolle-server.com"）
		// null または空文字列ならフィルタなし（従来の挙動）
		//public string HostSuffixFilter { get; set; } = null; // フィルターなし
		public string HostSuffixFilter { get; set; } = "kancolle-server.com";

		public void Add(CapturedHttp entry)
		{
			if (entry == null) return;

			// ホストフィルタが設定されている場合、対象外は破棄する
			if (!string.IsNullOrEmpty(this.HostSuffixFilter) && !IsHostMatch(entry.Url, this.HostSuffixFilter))
			{
				return;
			}

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

		private static bool IsHostMatch(string url, string hostSuffix)
		{
			if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(hostSuffix)) return false;
			try
			{
				if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
				{
					// パースできない場合は単純包含チェック（最終手段）
					return url.IndexOf(hostSuffix, StringComparison.OrdinalIgnoreCase) >= 0;
				}
				var host = uri.Host ?? string.Empty;
				// サブドメインを含めて末尾一致を確認（例: abc.kancolle-server.com）
				return host.Equals(hostSuffix, StringComparison.OrdinalIgnoreCase)
					|| host.EndsWith("." + hostSuffix, StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}
	}
}
