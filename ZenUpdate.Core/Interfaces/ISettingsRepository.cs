using ZenUpdate.Core.Models;

namespace ZenUpdate.Core.Interfaces;

/// <summary>
/// Provides read and write access to the user's application settings file.
/// Settings are stored at <c>%APPDATA%\XenUpdate\settings.json</c>.
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// Loads the current settings from disk.
    /// Returns default settings if the file does not exist yet.
    /// </summary>
    Task<AppSettings> LoadAsync();

    /// <summary>
    /// Saves the given settings to disk, overwriting the existing file.
    /// </summary>
    /// <param name="settings">The settings object to persist.</param>
    Task SaveAsync(AppSettings settings);
}
