using System;
using System.Windows;
using System.Windows.Data;

namespace Grabacr07.KanColleViewer.Views.Converters
{
	[ValueConversion(typeof(string), typeof(Visibility))]
	public class StringToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			var str = value as string;
			// 空文字列または null なら Collapsed、それ以外は Visible
			return string.IsNullOrEmpty(str) ? Visibility.Collapsed : Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
