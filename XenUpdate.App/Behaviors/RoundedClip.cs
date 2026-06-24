using System.Windows;
using System.Windows.Media;

namespace XenUpdate.App.Behaviors;

/// <summary>
/// Attached property that clips a <see cref="FrameworkElement"/> to a rounded rectangle that
/// tracks its current size.
///
/// WPF's <see cref="System.Windows.Controls.Border"/> paints rounded corners but does NOT clip
/// its child to them (<c>ClipToBounds</c> only clips to a plain rectangle), so square child
/// content — a DataGrid's surface fill and its header strip — bleeds past the rounded corners.
/// Setting <c>RoundedClip.Radius</c> on the frame applies a live rounded clip so the corners
/// stay clean as the element resizes.
/// </summary>
public static class RoundedClip
{
    /// <summary>The corner radius to clip the element to. 0 (default) means no clip.</summary>
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached(
            "Radius",
            typeof(double),
            typeof(RoundedClip),
            new PropertyMetadata(0.0, OnRadiusChanged));

    /// <summary>Gets the clip radius for <paramref name="element"/>.</summary>
    public static double GetRadius(DependencyObject element) => (double)element.GetValue(RadiusProperty);

    /// <summary>Sets the clip radius for <paramref name="element"/>.</summary>
    public static void SetRadius(DependencyObject element, double value) => element.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= OnSizeChanged;
        element.SizeChanged += OnSizeChanged;
        ApplyClip(element);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyClip((FrameworkElement)sender);

    private static void ApplyClip(FrameworkElement element)
    {
        var radius = GetRadius(element);
        if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            element.Clip = null;
            return;
        }

        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }
}
