using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Scans for and installs hardware driver updates by using the Windows Update API (WUApiLib).
/// Separate from <see cref="IWindowsUpdateService"/> because driver updates expose different
/// metadata and are presented on a dedicated page.
/// </summary>
public interface IDriverUpdateService
{
    /// <summary>
    /// Searches for available driver updates only.
    /// </summary>
    /// <param name="cancellationToken">Allows the caller to cancel the search.</param>
    /// <returns>A read-only list of available driver updates.</returns>
    Task<IReadOnlyList<DriverUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Downloads and installs a single driver update.
    /// </summary>
    /// <param name="update">The driver update to install.</param>
    /// <param name="progress">Reports internal install progress checkpoints as an integer percentage.</param>
    /// <param name="cancellationToken">Allows the caller to cancel the installation.</param>
    /// <returns>The installation result, including success state and reboot requirement.</returns>
    Task<DriverUpdateInstallResult> InstallUpdateAsync(
        DriverUpdateItem update,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}
