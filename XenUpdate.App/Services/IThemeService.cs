using System.Windows.Media;
using XenUpdate.Core.Enums;

namespace XenUpdate.App.Services;

/// <summary>
/// Applies the selected <see cref="AppTheme"/> to the running application
/// by swapping the active Zen* brush dictionary and Material Design base theme.
/// Implementations must be called on the UI thread.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Swaps the merged <c>ZenColors.*.xaml</c> resource dictionary and flips the
    /// Material Design base theme (Light/Dark) so every <c>DynamicResource</c>
    /// reference in the UI picks up the new palette immediately.
    /// </summary>
    /// <param name="theme">The theme to activate.</param>
    void ApplyTheme(AppTheme theme);

    /// <summary>
    /// Repoints every palette-tied Zen* brush app-wide — primary/secondary accents AND every
    /// background-derived surface, sidebar, divider, glass panel, and log-drawer/update-queue
    /// window brush — to the given colors, plus MaterialDesignThemes' own primary/secondary
    /// palette. Independent of <see cref="ApplyTheme"/> — the chosen palette stays the same
    /// across a Light/Dark switch, since it overrides individual keys directly on
    /// Application.Resources rather than living inside the swapped dictionary.
    /// </summary>
    /// <param name="primary">The main accent color.</param>
    /// <param name="secondary">An optional secondary accent. Null derives a lighter variant of <paramref name="primary"/> instead.</param>
    /// <param name="background">The base background color every surface/chrome brush is derived from.</param>
    void ApplyPalette(Color primary, Color? secondary, Color background);
}
