using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Checks whether a newer Visual Studio release is available for the installed edition, by
/// comparing vswhere's reported installed version against Microsoft's public channel manifest.
/// Fails soft: returns a not-<c>Checked</c> result rather than throwing when Visual Studio isn't
/// installed or the lookup is unavailable.
/// </summary>
public interface IVisualStudioUpdateService
{
    /// <summary>Checks for a newer Visual Studio release; never throws — returns Checked=false on any failure.</summary>
    Task<DriverUpdateStatus> CheckAsync(CancellationToken cancellationToken = default);
}
