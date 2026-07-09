using XenUpdate.Core.Enums;
using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Provides read and write access to the whitelist file: updates the user has pre-approved
/// to install themselves the instant the app detects it is online, with no manual click.
/// The file is stored at <c>%APPDATA%\XenUpdate\whitelist.json</c>.
/// </summary>
public interface IWhitelistRepository
{
    /// <summary>
    /// Raised after any successful write to the whitelist (add or remove).
    /// May fire from a background thread; UI subscribers must marshal to the UI thread.
    /// </summary>
    event Action? WhitelistChanged;

    /// <summary>Returns every whitelist entry across all sources.</summary>
    Task<IReadOnlyList<WhitelistEntry>> GetEntriesAsync();

    /// <summary>Returns the whitelisted update IDs for a single source (e.g. just winget package IDs).</summary>
    Task<IReadOnlyList<string>> GetWhitelistedIdsAsync(UpdateSource source);

    /// <summary>Adds an entry to the whitelist and saves the file. No-op if already present.</summary>
    Task AddAsync(UpdateSource source, string id, string displayName);

    /// <summary>Removes an entry from the whitelist and saves the file.</summary>
    Task RemoveAsync(UpdateSource source, string id);
}
