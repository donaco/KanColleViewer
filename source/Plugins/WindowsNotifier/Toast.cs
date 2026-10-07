using System;
using Grabacr07.KanColleViewer.Composition;

namespace Grabacr07.KanColleViewer.Plugins
{
	/// <summary>
	/// Windows のトースト通知機能を提供します。
	/// </summary>
	public class Toast
	{
		internal static bool IsWindows10OrLater => Environment.OSVersion.Version.Major >= 10;

		private readonly IAppNotificationService appNotificationService;

		/// <summary>
		/// トースト通知機能をサポートしているかどうかを示す値を取得します。
		/// </summary>
		/// <returns>
		/// 動作しているオペレーティング システムが Windows 10 以降で、ホストのトースト通知が利用可能な場合は true。
		/// </returns>
		public bool IsSupported => IsWindows10OrLater && this.appNotificationService.IsAvailable;

		public event Action? Activated;

		public event Action<Exception>? ToastFailed;

		private readonly string header;
		private readonly string body;
		public Toast(IAppNotificationService appNotificationService, string header, string body)
		{
			this.appNotificationService = appNotificationService;
			this.header = header;
			this.body = body;
		}

		public void Show()
		{
			this.appNotificationService.Show(this.header, this.body, this.Activated ?? (() => { }), this.ToastFailed);
		}
	}
}
