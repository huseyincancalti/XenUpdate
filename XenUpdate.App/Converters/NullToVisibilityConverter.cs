using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace XenUpdate.App.Converters;

/// <summary>
/// Visible when the bound value is non-null and, for strings specifically, non-empty/non-whitespace
/// (a "chosen" background image path that's an empty string should read the same as "none chosen").
/// Pass ConverterParameter="Invert" to flip it (Visible when null/empty instead).
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };

        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        if (invert)
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
