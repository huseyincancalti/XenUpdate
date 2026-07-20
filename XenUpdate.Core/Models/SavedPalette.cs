namespace XenUpdate.Core.Models;

/// <summary>
/// One user-saved appearance theme: the Primary/Secondary/Background trio captured from the
/// Appearance settings at the moment the user hit "save as theme". Stored (unlimited, local
/// only) in <see cref="AppSettings.SavedCustomPalettes"/> and rendered alongside the built-in
/// palette presets. Plain mutable POCO purely for JSON serialization.
/// </summary>
public sealed class SavedPalette
{
    /// <summary>Display name shown under the preset tile (e.g. "Custom 1" / "Özel 1").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The theme's primary accent color, as "#RRGGBB".</summary>
    public string PrimaryHex { get; set; } = string.Empty;

    /// <summary>The theme's secondary accent color, as "#RRGGBB". Null when it was saved with the auto-derived secondary.</summary>
    public string? SecondaryHex { get; set; }

    /// <summary>The theme's base background color, as "#RRGGBB".</summary>
    public string BackgroundHex { get; set; } = string.Empty;
}
