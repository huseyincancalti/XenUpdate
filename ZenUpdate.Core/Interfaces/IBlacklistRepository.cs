using ZenUpdate.Core.Models;

namespace ZenUpdate.Core.Interfaces;

/// <summary>
/// Provides read and write access to the blacklist file.
/// The blacklist stores package IDs that should be excluded from winget update results.
/// The file is stored at <c>%APPDATA%\XenUpdate\blacklist.json</c>.
/// </summary>
public interface IBlacklistRepository
{
    /// <summary>
    /// Raised after any successful write to the blacklist (add or remove).
    /// May fire from a background thread; UI subscribers must marshal to the UI thread.
    /// </summary>
    event Action? BlacklistChanged;

    /// <summary>
    /// Returns all blacklist entries including their optional reason text.
    /// </summary>
    Task<IReadOnlyList<BlacklistEntry>> GetEntriesAsync();

    /// <summary>
    /// Returns all currently blacklisted winget package IDs.
    /// </summary>
    Task<IReadOnlyList<string>> GetBlacklistedIdsAsync();

    /// <summary>
    /// Adds a package ID to the blacklist and saves the file.
    /// </summary>
    /// <param name="packageId">The winget package ID to blacklist. Example: "Microsoft.Teams"</param>
    /// <param name="reason">Optional free-form reason shown in the Settings UI.</param>
    Task AddAsync(string packageId, string? reason = null);

    /// <summary>
    /// Removes a package ID from the blacklist and saves the file.
    /// </summary>
    /// <param name="packageId">The winget package ID to remove from the blacklist.</param>
    Task RemoveAsync(string packageId);
}
