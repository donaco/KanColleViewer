using System;

namespace Grabacr07.KanColleViewer.Composition
{
	public interface IAppNotificationService
	{
		bool IsAvailable { get; }

		string ProductTitle { get; }

		void Show(string header, string body, Action activated, Action<Exception>? failed);
	}
}
