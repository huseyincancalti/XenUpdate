using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace XenUpdate.App.Controls;

/// <summary>
/// A self-drawn saturation/value square + hue strip + hex box, replacing
/// System.Windows.Forms.ColorDialog for in-app color selection. See the XAML file's header
/// comment for why this exists instead of the native dialog.
/// </summary>
public partial class ColorPickerControl : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPickerControl),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static readonly DependencyPropertyKey SelectedColorBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedColorBrush), typeof(Brush), typeof(ColorPickerControl), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty SelectedColorBrushProperty = SelectedColorBrushPropertyKey.DependencyProperty;

    public Brush SelectedColorBrush
    {
        get => (Brush)GetValue(SelectedColorBrushProperty);
        private set => SetValue(SelectedColorBrushPropertyKey, value);
    }

    // Kept separately from SelectedColor (rather than re-derived from it on every change) because
    // RGB->HSV is lossy at the achromatic edges: pure white/black/gray all report Hue = 0 (red),
    // which would silently snap the hue thumb back to red every time saturation or value hits
    // zero while dragging. Only re-derived when SelectedColor changes from OUTSIDE this control.
    private double _hue = 260;
    private double _saturation = 1;
    private double _value = 1;
    private bool _isInternalColorUpdate;

    public ColorPickerControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshVisuals();
        SvBorder.SizeChanged += (_, _) => PositionThumbs();
        HueBorder.SizeChanged += (_, _) => PositionThumbs();
        RefreshVisuals();
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ColorPickerControl)d;
        if (control._isInternalColorUpdate)
        {
            // The change originated from our own SV/hue drag — _hue/_saturation/_value are
            // already authoritative, re-deriving them from the resulting RGB would lose hue at
            // achromatic points instead of just refreshing the visuals for what's already set.
            return;
        }

        var (h, s, v) = RgbToHsv((Color)e.NewValue);
        control._hue = s < 0.0001 || v < 0.0001 ? control._hue : h;
        control._saturation = s;
        control._value = v;
        control.RefreshVisuals();
    }

    private void CommitFromHsv()
    {
        var color = HsvToRgb(_hue, _saturation, _value);
        _isInternalColorUpdate = true;
        try
        {
            SelectedColor = color;
        }
        finally
        {
            _isInternalColorUpdate = false;
        }

        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        var color = HsvToRgb(_hue, _saturation, _value);
        SelectedColorBrush = new SolidColorBrush(color);
        HueBaseRect.Fill = new SolidColorBrush(HsvToRgb(_hue, 1, 1));

        // Don't overwrite while the user is actually typing in the box — a programmatic set
        // resets the caret mid-edit. CommitHexText does its own unconditional rewrite on
        // Enter/LostFocus, so the box always resyncs once the edit ends.
        if (!HexTextBox.IsKeyboardFocusWithin)
        {
            HexTextBox.Text = ToHex(color);
        }

        PositionThumbs();
    }

    private void PositionThumbs()
    {
        var svWidth = SvBorder.ActualWidth;
        var svHeight = SvBorder.ActualHeight;
        if (svWidth > 0 && svHeight > 0)
        {
            Canvas.SetLeft(SvThumb, _saturation * svWidth);
            Canvas.SetTop(SvThumb, (1 - _value) * svHeight);
        }

        var hueWidth = HueBorder.ActualWidth;
        if (hueWidth > 0)
        {
            Canvas.SetLeft(HueThumb, (_hue / 360.0) * hueWidth);
        }
    }

    private void SvBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SvBorder.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvBorder));
    }

    private void SvBorder_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (SvBorder.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSvFromPoint(e.GetPosition(SvBorder));
        }
    }

    private void SvBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SvBorder.ReleaseMouseCapture();

    private void SvBorder_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        // No-op: just needs to exist so an interrupted drag (e.g. Alt+Tab mid-drag) doesn't leave capture stuck.
    }

    private void UpdateSvFromPoint(Point position)
    {
        var width = SvBorder.ActualWidth;
        var height = SvBorder.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _saturation = Clamp01(position.X / width);
        _value = 1 - Clamp01(position.Y / height);
        CommitFromHsv();
    }

    private void HueBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueBorder.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueBorder));
    }

    private void HueBorder_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (HueBorder.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateHueFromPoint(e.GetPosition(HueBorder));
        }
    }

    private void HueBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => HueBorder.ReleaseMouseCapture();

    private void HueBorder_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
    }

    private void UpdateHueFromPoint(Point position)
    {
        var width = HueBorder.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        _hue = Clamp01(position.X / width) * 360.0;
        CommitFromHsv();
    }

    private void HexTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHexText();
            e.Handled = true;
        }
    }

    private void HexTextBox_OnLostFocus(object sender, RoutedEventArgs e) => CommitHexText();

    private void CommitHexText()
    {
        var text = HexTextBox.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        try
        {
            // Force opaque: ColorConverter accepts #AARRGGBB too, but everything downstream
            // (settings hex strings, luminance-based theme sync, derived surface brushes)
            // assumes an opaque color — a stray alpha here would silently produce a
            // semi-transparent "background color" with no UI concept to explain it.
            var parsed = (Color)ColorConverter.ConvertFromString(text)!;
            SelectedColor = Color.FromRgb(parsed.R, parsed.G, parsed.B);
        }
        catch
        {
            // Not a parseable color — fall through to the resync below; the user's likely
            // still mid-edit anyway if this fires from LostFocus.
        }

        // Always rewrite the box directly. Relying on the HexText binding alone left stale text
        // behind whenever the property VALUE didn't change — e.g. typing garbage (HexText was
        // already the last good value, so no change notification fired) or typing the current
        // color in a different spelling ("fff" while on #FFFFFF) — and the box kept showing the
        // raw input instead of the canonical code.
        HexTextBox.Text = ToHex(SelectedColor);
    }

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static (double Hue, double Saturation, double Value) RgbToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue = 0;
        if (delta > 0.00001)
        {
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max <= 0 ? 0 : delta / max;
        var value = max;
        return (hue, saturation, value);
    }

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;

        var (r1, g1, b1) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        var r = (byte)Math.Round((r1 + m) * 255);
        var g = (byte)Math.Round((g1 + m) * 255);
        var b = (byte)Math.Round((b1 + m) * 255);
        return Color.FromRgb(r, g, b);
    }
}
