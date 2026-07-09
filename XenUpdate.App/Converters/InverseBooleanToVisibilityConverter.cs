using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace XenUpdate.App.Converters;

/// <summary>
/// The opposite of the built-in <see cref="BooleanToVisibilityConverter"/>: true collapses,
/// false shows. Used where a single bool (e.g. "is a guide selected?") controls two mutually
/// exclusive panels — one bound directly, the other through this converter.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible ? false : true;
}
