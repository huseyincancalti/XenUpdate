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
}
