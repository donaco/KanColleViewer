using System;
using System.Collections.Concurrent;
using System.Threading;
using CommunityToolkit.WinUI.Notifications;

namespace Grabacr07.KanColleViewer.Plugins
{
	/// <summary>
	/// Windows のトースト通知機能を提供します。
	/// </summary>
	public class Toast
	{
		#region static members

		/// <summary>
		/// トースト通知機能をサポートしているかどうかを示す値を取得します。
		/// </summary>
		/// <returns>
		/// 動作しているオペレーティング システムが Windows 10 以降の場合は true、それ以外の場合は false。
		/// </returns>
		public static bool IsSupported => Environment.OSVersion.Version.Major >= 10;

		/// <summary>
		/// 通知がクリックされたときに、対応する <see cref="Toast"/> を解決するためのテーブル。
		/// </summary>
		private static readonly ConcurrentDictionary<string, Toast> toasts = new ConcurrentDictionary<string, Toast>();

		private static int isHandlerRegistered;

		private const string TagKey = "kcv-toast-id";

		private static void EnsureHandlerRegistered()
		{
			if (Interlocked.Exchange(ref isHandlerRegistered, 1) != 0) return;

			ToastNotificationManagerCompat.OnActivated += e =>
			{
				var args = ToastArguments.Parse(e.Argument);
				if (!args.Contains(TagKey)) return;

				Toast toast;
				if (toasts.TryRemove(args[TagKey], out toast))
				{
					toast.Activated?.Invoke();
				}
			};
		}

		#endregion

		public event Action Activated;

		public event Action<Exception> ToastFailed;

		private readonly string header;
		private readonly string body;
		private readonly string id = Guid.NewGuid().ToString("N");

		public Toast(string header, string body)
		{
			this.header = header;
			this.body = body;
		}

		public void Show()
		{
			try
			{
				EnsureHandlerRegistered();
				toasts[this.id] = this;

				new ToastContentBuilder()
					.AddArgument(TagKey, this.id)
					.AddText(this.header)
					.AddText(this.body)
					.Show();
			}
			catch (Exception ex)
			{
				Toast removed;
				toasts.TryRemove(this.id, out removed);
				this.ToastFailed?.Invoke(ex);
			}
		}
	}
}
