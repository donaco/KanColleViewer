using System;
using System.Linq;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleViewer.Models;

namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public static class ShipSpeedExtensions
	{
		public static string ToDisplayString(this ShipSpeed? speed) => ToDisplayString(speed ?? ShipSpeed.Immovable);

		public static string ToDisplayString(this ShipSpeed speed)
		{
			switch (speed)
			{
				case ShipSpeed.Fastest:
					return ResourceService.Current["Ui_ShipCatalog_Fastest"];
				case ShipSpeed.Faster:
					return ResourceService.Current["Ui_ShipCatalog_Faster"];
				case ShipSpeed.Fast:
					return ResourceService.Current["Ui_ShipCatalog_Fast"];
				case ShipSpeed.Slow:
					return ResourceService.Current["Ui_ShipCatalog_Slow"];
				default:
					return "";
			}
		}
	}
}
