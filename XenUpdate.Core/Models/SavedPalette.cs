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

    public string PrimaryHex { get; set; } = string.Empty;

    /// <summary>Null when the theme was saved with the auto-derived secondary.</summary>
    public string? SecondaryHex { get; set; }

    public string BackgroundHex { get; set; } = string.Empty;
}
