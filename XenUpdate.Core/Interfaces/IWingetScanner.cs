using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Scans for available application updates using the winget package manager.
/// Implementations should filter out blacklisted packages before returning results.
/// </summary>
public interface IWingetScanner
{
    /// <summary>
    /// Runs "winget upgrade" in the background and returns all available
    /// non-blacklisted application updates.
    /// </summary>
    /// <param name="cancellationToken">Allows the caller to cancel the scan operation.</param>
    /// <returns>A read-only list of available application updates.</returns>
    Task<IReadOnlyList<AppUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken);
}
