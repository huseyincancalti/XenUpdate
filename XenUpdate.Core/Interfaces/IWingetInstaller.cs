using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Installs a single application update using the winget package manager.
/// </summary>
public interface IWingetInstaller
{
    /// <summary>
    /// Runs "winget upgrade --id {packageId} --silent" for a given application update.
    /// </summary>
    /// <param name="item">The application update item to install.</param>
    /// <param name="progress">
    /// Reports installation progress: completion percentage plus the live download
    /// size when winget emits it. The UI uses this to update the progress bar and the
    /// "downloaded / total" readout.
    /// </param>
    /// <param name="cancellationToken">Allows the caller to cancel the installation.</param>
    /// <returns>True if the installation succeeded; false otherwise.</returns>
    Task<bool> InstallUpdateAsync(
        AppUpdateItem item,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken);
}
