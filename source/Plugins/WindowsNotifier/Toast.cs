using System;
using Grabacr07.KanColleViewer.Services;

namespace Grabacr07.KanColleViewer.Plugins
{
	/// <summary>
	/// Windows のトースト通知機能を提供します。
	/// </summary>
	public class Toast
	{
		private static readonly bool IsWindows10OrLater = Environment.OSVersion.Version.Major >= 10;

		/// <summary>
		/// トースト通知機能をサポートしているかどうかを示す値を取得します。
		/// </summary>
		/// <returns>
		/// 動作しているオペレーティング システムが Windows 10 以降の場合は true、それ以外の場合は false。
		/// </returns>
		public static bool IsSupported => IsWindows10OrLater && AppNotificationService.IsAvailable;

		public event Action Activated;

		public event Action<Exception> ToastFailed;

		private readonly string header;
		private readonly string body;
		public Toast(string header, string body)
		{
			this.header = header;
			this.body = body;
		}

		public void Show()
		{
			AppNotificationService.Show(this.header, this.body, this.Activated, this.ToastFailed);
		}
	}
}
