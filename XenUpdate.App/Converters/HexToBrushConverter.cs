using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace XenUpdate.App.Converters;

/// <summary>
/// "#RRGGBB" string → frozen SolidColorBrush, for elements bound to raw hex strings (the saved
/// color swatches in Settings). Returns Transparent on anything unparseable instead of
/// throwing, so one corrupt saved value can't take the whole panel down.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : $"#{hex}")!;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                // fall through to Transparent
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
