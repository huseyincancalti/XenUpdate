using System;
using System.Diagnostics;
using System.Windows;
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
/// Both steps are independently guarded so a failure in one never aborts the other,
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
}
