using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XenUpdate.App.Views;

public partial class AppBackgroundLayer : UserControl
{
    /// <summary>
    /// Whether the photo+blur+scrim layer shows at all (the ambient gradient fallback and
    /// spotlight are unaffected). Default true. Set false for small, rounded-corner utility
    /// windows (Log Viewer, Update Queue) where a hard-edged photo rectangle would visibly
    /// poke past the card's rounded corners instead of being clipped to them.
    /// </summary>
    public static readonly DependencyProperty ShowPhotoBackdropProperty = DependencyProperty.Register(
        nameof(ShowPhotoBackdrop),
        typeof(bool),
        typeof(AppBackgroundLayer),
        new PropertyMetadata(true, OnShowPhotoBackdropChanged));

    public bool ShowPhotoBackdrop
    {
        get => (bool)GetValue(ShowPhotoBackdropProperty);
        set => SetValue(ShowPhotoBackdropProperty, value);
    }

    private static void OnShowPhotoBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AppBackgroundLayer control)
        {
            control.PhotoLayerGrid.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Whether the base ZenAmbientBackgroundBrush fallback rectangle shows. Default true. Set
    /// false alongside <see cref="ShowPhotoBackdrop"/> for windows that already supply their own
    /// solid background (Log Viewer, Update Queue) and only want the spotlight glow layered on
    /// top of it, not this control's own gradient painted underneath their content.
    /// </summary>
    public static readonly DependencyProperty ShowAmbientFallbackProperty = DependencyProperty.Register(
        nameof(ShowAmbientFallback),
        typeof(bool),
        typeof(AppBackgroundLayer),
        new PropertyMetadata(true, OnShowAmbientFallbackChanged));

    public bool ShowAmbientFallback
    {
        get => (bool)GetValue(ShowAmbientFallbackProperty);
        set => SetValue(ShowAmbientFallbackProperty, value);
    }

    private static void OnShowAmbientFallbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AppBackgroundLayer control)
        {
            control.AmbientFallbackRect.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public AppBackgroundLayer()
    {
        InitializeComponent();

        // Absolute mapping so Center/GradientOrigin are plain DIPs matching whatever coordinate
        // the owning window reports from GetPosition — relative (0-1) mapping would need
        // re-deriving a fraction from ActualWidth/Height on every mouse move for no benefit.
        SpotlightBrush.MappingMode = BrushMappingMode.Absolute;
        SpotlightBrush.RadiusX = 260;
        SpotlightBrush.RadiusY = 260;
    }

    /// <summary>
    /// Moves the spotlight glow to <paramref name="position"/> (window-local DIPs). Call this
    /// from the owning window's PreviewMouseMove — the control itself is IsHitTestVisible="False"
    /// throughout so it never receives mouse events on its own.
    /// </summary>
    public void UpdateSpotlightPosition(Point position)
    {
        SpotlightBrush.Center = position;
        SpotlightBrush.GradientOrigin = position;
    }
}
