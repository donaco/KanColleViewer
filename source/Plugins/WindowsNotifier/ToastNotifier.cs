using System;

namespace Grabacr07.KanColleViewer.Plugins
{
	internal class ToastNotifier : NotifierBase
	{
		public override bool IsSupported => Toast.IsWindows10OrLater && this.AppNotificationService.IsAvailable;

		protected override void InitializeCore() {}

		protected override void NotifyCore(string header, string body, Action? activated, Action<Exception>? failed)
		{
			var toast = this.CreateToast(header, body);
			toast.Activated += activated;
			if (failed != null) toast.ToastFailed += ex => failed(ex);

			toast.Show();
		}

		private Toast CreateToast(string header, string body) => new Toast(this.AppNotificationService, header, body);
	}
}
