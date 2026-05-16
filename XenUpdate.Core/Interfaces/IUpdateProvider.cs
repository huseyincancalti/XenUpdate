using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Contract for a service that can discover available updates and install them.
/// Implement once per update source (Winget, Windows Update, Drivers, HardwareHub).
/// </summary>
public interface IUpdateProvider
{
    /// <summary>
    /// Scans the system for available updates and returns them as a flat list.
    /// Implementations must not throw; return an empty enumerable on failure
    /// and log the error via <c>ILoggerService</c>.
    /// </summary>
    Task<IEnumerable<CategorizedUpdateItem>> GetUpdatesAsync();

    /// <summary>
    /// Installs the specified update item.
    /// The implementation is responsible for updating <see cref="CategorizedUpdateItem.Status"/>
    /// to <c>Downloading</c>, <c>Installing</c>, <c>Installed</c>, or <c>Failed</c> as progress changes.
    /// </summary>
    Task<bool> InstallUpdateAsync(CategorizedUpdateItem item, IProgress<double> progress);
}
