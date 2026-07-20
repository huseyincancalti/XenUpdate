using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace XenUpdate.App.Converters;

/// <summary>
/// Loads a file path into a frozen, in-memory BitmapImage for binding to Image.Source.
/// CacheOption.OnLoad + Freeze() means the file handle is released immediately after decode
/// (so the user can move/delete the source photo without the app holding it open) and the
/// result is safe to hand across threads. Null or invalid paths return null rather than
/// throwing, so a stale/deleted background photo path just shows nothing instead of crashing.
/// </summary>
public sealed class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
