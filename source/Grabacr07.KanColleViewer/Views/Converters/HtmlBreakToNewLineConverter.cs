using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Grabacr07.KanColleViewer.Views.Converters
{
	public class HtmlBreakToNewLineConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			var text = value as string;
			if (string.IsNullOrEmpty(text)) return value;

			// <br>, <br/>, <br /> などのバリエーションを削除
			return Regex.Replace(text, @"<br\s*/?>", "", RegexOptions.IgnoreCase);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
