using System;
using System.Windows.Data;
using Grabacr07.KanColleWrapper.Models;

namespace Grabacr07.KanColleViewer.Views.Converters
{
	[ValueConversion(typeof(string), typeof(SlotItemIconType))]
	public class StringToSlotItemIconTypeConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			var str = value as string;

			// 空文字列または null なら Unknown を返す
			if (string.IsNullOrEmpty(str))
			{
				return SlotItemIconType.Unknown;
			}

			// 文字列を SlotItemIconType に変換
			try
			{
				return (SlotItemIconType)Enum.Parse(typeof(SlotItemIconType), str, ignoreCase: true);
			}
			catch
			{
				return SlotItemIconType.Unknown;
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
