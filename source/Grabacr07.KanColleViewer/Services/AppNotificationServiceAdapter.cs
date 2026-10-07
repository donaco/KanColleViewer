using System;
using System.ComponentModel.Composition;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Models;

namespace Grabacr07.KanColleViewer.Services
{
	[Export(typeof(IAppNotificationService))]
	internal sealed class AppNotificationServiceAdapter : IAppNotificationService
	{
		public bool IsAvailable => AppNotificationService.IsAvailable;

		public string ProductTitle => ProductInfo.Title;

		public void Show(string header, string body, Action activated, Action<Exception>? failed)
		{
			AppNotificationService.Show(header, body, activated, failed);
		}
	}
}
