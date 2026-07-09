using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Installs a single Python package update using pip.
/// </summary>
public interface IPipInstaller
{
    /// <summary>
    /// Runs "pip install --upgrade {packageName}" for a given package update.
    /// </summary>
    /// <param name="item">The package update to install.</param>
    /// <param name="progress">Reports installation progress (percent only — pip has no reliable live byte counter to parse).</param>
    /// <param name="cancellationToken">Allows the caller to cancel the installation.</param>
    /// <returns>True if the installation succeeded; false otherwise.</returns>
    Task<bool> InstallUpdateAsync(
        PipPackageItem item,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken);
}
