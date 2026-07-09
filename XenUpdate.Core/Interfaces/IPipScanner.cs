using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Scans for outdated Python packages using pip.
/// </summary>
public interface IPipScanner
{
    /// <summary>
    /// Runs "pip list --outdated" in the background and returns every outdated package.
    /// </summary>
    /// <param name="cancellationToken">Allows the caller to cancel the scan operation.</param>
    /// <returns>A read-only list of available package updates.</returns>
    Task<IReadOnlyList<PipPackageItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken);
}
