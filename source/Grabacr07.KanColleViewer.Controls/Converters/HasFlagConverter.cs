using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Grabacr07.KanColleViewer.Converters
{
	public class HasFlagConverter : IValueConverter
	{
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			try
			{
				if (value is Enum enumValue && parameter is string p)
				{
					return p.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(x => x.Trim())
						.All(x => enumValue.HasFlag((Enum)Enum.Parse(enumValue.GetType(), x)));
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.Write(ex);
			}

			return false;
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
