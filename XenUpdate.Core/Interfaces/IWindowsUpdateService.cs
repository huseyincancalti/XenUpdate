using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Scans for and installs Windows OS updates using the Windows Update API (WUApiLib).
/// Both operations use the same WUApiLib session, so they are combined in one interface.
/// </summary>
public interface IWindowsUpdateService
{
    /// <summary>
    /// Searches for available Windows OS updates (excludes drivers).
    /// This can take 30–120 seconds on the first run.
    /// </summary>
    /// <param name="cancellationToken">Allows the caller to cancel the search.</param>
    /// <returns>A read-only list of available OS updates.</returns>
    Task<IReadOnlyList<WindowsUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Downloads and installs a single Windows OS update.
    /// </summary>
    /// <param name="update">The OS update to install.</param>
    /// <param name="progress">Reports installation progress as an integer percentage (0–100).</param>
    /// <param name="cancellationToken">Allows the caller to cancel the installation.</param>
    /// <returns>The installation result, including success state and reboot requirement.</returns>
    Task<WindowsUpdateInstallResult> InstallUpdateAsync(
        WindowsUpdateItem update,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}
