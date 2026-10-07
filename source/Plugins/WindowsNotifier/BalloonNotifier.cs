using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grabacr07.KanColleViewer.Models;

namespace Grabacr07.KanColleViewer.Plugins
{
	internal class BalloonNotifier : NotifierBase
	{
		private NotifyIcon? notifyIcon;
		private Icon? icon;
		private EventHandler? activatedAction;

		public override bool IsSupported => !Toast.IsSupported;

		protected override void InitializeCore()
		{
			const string iconUri = "pack://application:,,,/KanColleViewer;Component/Assets/app.ico";

			Uri? uri;
			if (!Uri.TryCreate(iconUri, UriKind.Absolute, out uri))
				return;

			var streamResourceInfo = System.Windows.Application.GetResourceStream(uri);
			if (streamResourceInfo == null)
				return;

			System.Windows.Application.Current.Dispatcher.Invoke(() =>
			{
				using (var stream = streamResourceInfo.Stream)
				{
					this.icon = new Icon(stream);
					this.notifyIcon = new NotifyIcon
					{
						Text = ProductInfo.Title,
						Icon = this.icon,
						Visible = true,
					};
				}
			});
		}

		protected override void NotifyCore(string header, string body, Action? activated, Action<Exception>? failed)
		{
			if (this.notifyIcon == null) return;

			System.Windows.Application.Current.Dispatcher.Invoke(() =>
			{
				this.notifyIcon.BalloonTipClicked -= this.activatedAction;
				this.activatedAction = activated == null ? null : (sender, args) => activated();
				if (this.activatedAction != null)
					this.notifyIcon.BalloonTipClicked += this.activatedAction;

				this.notifyIcon.ShowBalloonTip(1000, header, body, ToolTipIcon.None);
			});
		}

		public override void Dispose()
		{
			if (this.notifyIcon != null)
			{
				this.notifyIcon.Visible = false;
				this.notifyIcon.Dispose();
				this.notifyIcon = null;
			}

			this.icon?.Dispose();
			this.icon = null;
		}
	}
}
