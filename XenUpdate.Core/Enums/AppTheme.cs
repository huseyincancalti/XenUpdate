namespace XenUpdate.Core.Enums;

/// <summary>
/// Identifies which visual theme the application is currently displaying.
/// Persisted as part of <see cref="Models.AppSettings"/>.
/// </summary>
public enum AppTheme
{
    /// <summary>The default dark color palette.</summary>
    Dark = 0,

    /// <summary>The light color palette.</summary>
    Light = 1
}
