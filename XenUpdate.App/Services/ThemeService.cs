using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using XenUpdate.Core.Enums;

namespace XenUpdate.App.Services;

/// <summary>
/// Default <see cref="IThemeService"/> implementation. Works by:
///   1. Replacing the currently merged <c>ZenColors.*.xaml</c> dictionary on
///      <see cref="Application.Current"/>, which carries the Zen* brushes used
///      throughout the app via <c>DynamicResource</c>.
///   2. Asking <see cref="PaletteHelper"/> to flip Material Design's base theme
///      so bundled brushes (MaterialDesignPaper, MaterialDesignBody, etc.) follow.
///   3. Deriving every background-tied Zen* brush (surfaces, sidebar, dividers, glass
///      panels, the log drawer/update queue windows' chrome) from one chosen background
///      color via lighten/darken, so the whole app shares one palette — not just the accent.
/// Every step is independently guarded so a failure in one never aborts the others,
/// and a complete failure never blocks application startup.
/// </summary>
public sealed class ThemeService : IThemeService
{
    /// <summary>A known key that only lives in the Zen theme dictionaries; used to locate the active one.</summary>
    private const string ZenThemeMarkerKey = "ZenBackgroundBrush";

    private static readonly Uri DarkThemeUri =
        new("pack://application:,,,/XenUpdate;component/Themes/ZenColors.Dark.xaml", UriKind.Absolute);

    private static readonly Uri LightThemeUri =
        new("pack://application:,,,/XenUpdate;component/Themes/ZenColors.Light.xaml", UriKind.Absolute);

    /// <inheritdoc />
    public void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        // Each step is wrapped separately. If the Zen brushes swap fails, we
        // still try to flip the Material Design base theme (and vice versa),
        // and as a last resort we attempt to fall back to Dark. Under no
        // circumstances does a theme failure propagate up to OnStartup.
        var zenSwapOk = TryReplaceZenDictionary(app, theme);
        var mdSwapOk = TryApplyMaterialDesignBaseTheme(theme);

        if ((!zenSwapOk || !mdSwapOk) && theme != AppTheme.Dark)
        {
            Debug.WriteLine($"[XenUpdate] Theme '{theme}' failed to apply; reverting to Dark.");
            TryReplaceZenDictionary(app, AppTheme.Dark);
            TryApplyMaterialDesignBaseTheme(AppTheme.Dark);
        }
    }

    private static bool TryReplaceZenDictionary(Application app, AppTheme theme)
    {
        try
        {
            var uri = theme == AppTheme.Light ? LightThemeUri : DarkThemeUri;
            var newDict = new ResourceDictionary { Source = uri };

            var dictionaries = app.Resources.MergedDictionaries;
            for (var i = 0; i < dictionaries.Count; i++)
            {
                if (dictionaries[i].Contains(ZenThemeMarkerKey))
                {
                    dictionaries[i] = newDict;
                    return true;
                }
            }

            dictionaries.Add(newDict);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] ReplaceZenDictionary failed for '{theme}': {ex.Message}");
            return false;
        }
    }

    private static bool TryApplyMaterialDesignBaseTheme(AppTheme theme)
    {
        try
        {
            var paletteHelper = new PaletteHelper();
            var mdTheme = paletteHelper.GetTheme();
            mdTheme.SetBaseTheme(theme == AppTheme.Light ? BaseTheme.Light : BaseTheme.Dark);
            paletteHelper.SetTheme(mdTheme);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] MaterialDesign base theme flip failed for '{theme}': {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void ApplyPalette(Color primary, Color? secondary, Color background)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var effectiveSecondary = secondary ?? Lighten(primary, 0.25);

        // MaterialDesignThemes' own bundled brushes (button ripples, PrimaryHueXBrush, etc.)
        // follow through its own palette API — SetPrimaryColor/SetSecondaryColor derive the
        // light/mid/dark tonal trio from these two colors the same way they already do for the
        // built-in Material swatches, so buttons/sliders/etc. stay visually consistent.
        try
        {
            var paletteHelper = new PaletteHelper();
            var mdTheme = paletteHelper.GetTheme();
            mdTheme.SetPrimaryColor(primary);
            mdTheme.SetSecondaryColor(effectiveSecondary);
            paletteHelper.SetTheme(mdTheme);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] MaterialDesign primary/secondary color apply failed: {ex.Message}");
        }

        // Every Zen* brush below is set as a DIRECT entry on Application.Resources (not inside
        // a merged dictionary), which WPF resolves before checking merged dictionaries — so
        // these overrides survive independently of whichever ZenColors.*.xaml ApplyTheme has
        // merged in, and a Light/Dark switch never resets the chosen palette.
        try
        {
            app.Resources["ZenPrimaryBrush"] = new SolidColorBrush(primary);
            app.Resources["ZenPrimaryColor"] = primary;
            app.Resources["ZenSecondaryBrush"] = new SolidColorBrush(effectiveSecondary);
            app.Resources["ZenSecondaryColor"] = effectiveSecondary;
            app.Resources["ZenSidebarSelectedBrush"] = new SolidColorBrush(primary);
            app.Resources["ZenNavItemSelectedBackgroundBrush"] = new LinearGradientBrush(
                primary, effectiveSecondary, new Point(0, 0), new Point(1, 0.5));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Zen primary/secondary brush apply failed: {ex.Message}");
        }

        try
        {
            ApplyBackgroundDerivedBrushes(app, background, primary);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Zen background-derived brush apply failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Derives and sets every background-tied Zen* brush from one base color. This is what
    /// makes the Update Queue and Log Viewer windows (and every DataGrid, glass panel, and
    /// sidebar surface) follow the chosen palette instead of staying on the fixed hex baked
    /// into ZenColors.*.xaml — previously only the accent was customizable, so a background
    /// that was always purple-tinted made any non-purple accent look like it didn't belong.
    /// </summary>
    private static void ApplyBackgroundDerivedBrushes(Application app, Color background, Color primary)
    {
        // Every derivation below used to be a fixed Lighten-for-"raised"/Darken-for-"recessed"
        // direction, which only holds for dark backgrounds. Once backgrounds could be
        // user-chosen (including near-white ones like the Daylight/Linen presets), the "recessed"
        // brushes (matte log drawer, DataGrid rows, deep background) kept darkening toward black
        // regardless — producing near-black panels under a Light theme, whose text is dark
        // (#334155), i.e. dark-on-near-black. That's the same unreadable-text failure mode the
        // background-luminance theme auto-sync exists to prevent, just one level deeper (inside
        // panels, not the base surface). Raise/Recede below pick the blend direction from the
        // background's own luminance instead of hardcoding one: a "recessed" panel moves toward
        // whichever extreme (black or white) that theme's text color already contrasts against.
        var isLightBackground = Luminance(background) > 0.6;
        Color Raise(Color c, double amount) => isLightBackground ? Darken(c, amount) : Lighten(c, amount);
        Color Recede(Color c, double amount) => isLightBackground ? Lighten(c, amount) : Darken(c, amount);

        var surface = Raise(background, 0.10);
        var sidebarHover = Raise(background, 0.22);
        var sidebarCard = Raise(background, 0.14);
        var divider = Raise(background, 0.08);
        var deep = Recede(background, 0.55);
        var matte = Recede(background, 0.60);
        var matteHover = Recede(background, 0.45);
        var gridBody = Recede(background, 0.35);
        var gridHeader = Raise(background, 0.10);
        var sidebarPanelTop = Raise(background, 0.05);
        var ambientTint = Mix(background, primary, 0.16);
        var ambientDark = Raise(background, 0.08);

        app.Resources["ZenBackgroundBrush"] = new SolidColorBrush(background);
        app.Resources["ZenDeepBackgroundBrush"] = new SolidColorBrush(deep);
        app.Resources["ZenSurfaceBrush"] = new SolidColorBrush(surface);

        // Sidebar surfaces are deliberately translucent (glassmorphism): the blurred background
        // photo layer sits behind the whole window, and an opaque sidebar just walls it off.
        // Noticeably MORE opaque than the content-area glass (ZenGlassPanelBrush at 0x3A) —
        // the sidebar is dense with small nav text, so it needs a stronger backing to stay
        // readable over an arbitrary user photo, but still lets the photo bleed through.
        app.Resources["ZenSidebarBrush"] = new SolidColorBrush(WithRgb(surface, 0xC8));
        app.Resources["ZenSidebarHoverBrush"] = new SolidColorBrush(WithRgb(sidebarHover, 0xD5));
        app.Resources["ZenSidebarCardBrush"] = new SolidColorBrush(WithRgb(sidebarCard, 0xAA));
        app.Resources["ZenDividerBrush"] = new SolidColorBrush(divider);

        // A subtle wash of the accent through the ambient gradient — enough to feel tied to
        // the chosen primary color, not so much that the background competes with it.
        app.Resources["ZenAmbientBackgroundBrush"] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops =
            {
                new GradientStop(ambientDark, 0),
                new GradientStop(ambientTint, 0.52),
                new GradientStop(background, 1),
            },
        };

        // Same glassmorphism treatment as the sidebar card brushes above — translucent enough
        // for the photo to show through, opaque enough to carry the nav text on top of it.
        app.Resources["ZenSidebarPanelBrush"] = new LinearGradientBrush(
            WithRgb(sidebarPanelTop, 0xC8), WithRgb(surface, 0xD5), new Point(0, 0), new Point(0, 1));

        // Glass/chrome fills keep their original alpha (the translucency is what makes them
        // read as "glass") but swap in the new background-derived RGB. White-tinted glass
        // borders/hover highlights are theme-neutral by design and stay untouched.
        app.Resources["ZenGlassPanelBrush"] = new SolidColorBrush(WithRgb(surface, 0x3A));
        app.Resources["ZenDataGridChromeBackgroundBrush"] = new SolidColorBrush(WithRgb(surface, 0x22));
        app.Resources["ZenDataGridSurfaceBrush"] = new SolidColorBrush(WithRgb(gridBody, 0xE0));
        app.Resources["ZenDataGridHeaderBrush"] = new SolidColorBrush(WithRgb(gridHeader, 0x18));
        app.Resources["ZenDataGridRowSelectedBrush"] = new SolidColorBrush(WithRgb(primary, 0x33));

        // Deliberately flat/matte (not glass) per the log drawer's original design intent —
        // still derived from the chosen background so it no longer looks unrelated to it.
        app.Resources["ZenLogDrawerMatteBrush"] = new SolidColorBrush(matte);
        app.Resources["ZenLogDrawerHeaderHoverBrush"] = new SolidColorBrush(matteHover);
    }

    /// <summary>Blends a color toward white by <paramref name="amount"/> (0-1). Simple RGB-space lerp — not true HSL lightness, but visually reasonable and cheap to get right.</summary>
    private static Color Lighten(Color color, double amount)
    {
        byte Blend(byte channel) => (byte)(channel + (255 - channel) * amount);
        return Color.FromRgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    /// <summary>Blends a color toward black by <paramref name="amount"/> (0-1).</summary>
    private static Color Darken(Color color, double amount)
    {
        byte Blend(byte channel) => (byte)(channel * (1 - amount));
        return Color.FromRgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    /// <summary>Linearly interpolates from <paramref name="a"/> toward <paramref name="b"/> by <paramref name="t"/> (0-1).</summary>
    private static Color Mix(Color a, Color b, double t)
    {
        byte Blend(byte from, byte to) => (byte)(from + (to - from) * t);
        return Color.FromRgb(Blend(a.R, b.R), Blend(a.G, b.G), Blend(a.B, b.B));
    }

    /// <summary>Returns <paramref name="color"/>'s RGB with a specific alpha byte — used to re-tint an existing translucent brush's color while preserving exactly how translucent it was.</summary>
    private static Color WithRgb(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Standard perceptual luminance (0=black, 1=white); same formula/threshold used everywhere else in the app that decides Light vs Dark from a background color.</summary>
    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
