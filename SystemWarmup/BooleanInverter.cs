using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SystemWarmup;
public sealed class BooleanInverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}