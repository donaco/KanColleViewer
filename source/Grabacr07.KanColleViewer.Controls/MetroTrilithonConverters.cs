using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MetroTrilithon.UI.Converters
{
    // MetroTrilithon.Desktop Converters の内製化 (Phase 1)

    public class UniversalBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var bValue = value is bool b && b;
            Visibility result;

            if (bValue)
            {
                result = Visibility.Visible;
                var p = (parameter as string)?.Split(':');
                if (p?.Length >= 1)
                {
                    if (string.Compare(p[0], "Hidden", StringComparison.InvariantCultureIgnoreCase) == 0) result = Visibility.Hidden;
                    else if (string.Compare(p[0], "Collapsed", StringComparison.InvariantCultureIgnoreCase) == 0) result = Visibility.Collapsed;
                }
            }
            else
            {
                result = Visibility.Collapsed;
                var p = (parameter as string)?.Split(':');
                if (p?.Length >= 2)
                {
                    if (string.Compare(p[1], "Visible", StringComparison.InvariantCultureIgnoreCase) == 0) result = Visibility.Visible;
                    else if (string.Compare(p[1], "Hidden", StringComparison.InvariantCultureIgnoreCase) == 0) result = Visibility.Hidden;
                }
            }
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ReverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    public class StringToVisiblityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.Equals(parameter) ?? false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? parameter : Binding.DoNothing;
    }
}
