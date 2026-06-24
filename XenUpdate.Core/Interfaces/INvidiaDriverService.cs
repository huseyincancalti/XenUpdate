using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Checks whether a newer NVIDIA GPU driver is available by comparing the installed version
/// against NVIDIA's driver-lookup service. Fails soft: returns a not-<c>Checked</c> result rather
/// than throwing when the GPU isn't NVIDIA or the lookup is unavailable.
/// </summary>
public interface INvidiaDriverService
{
    /// <summary>Checks for a newer NVIDIA driver; never throws — returns Checked=false on any failure.</summary>
    Task<DriverUpdateStatus> CheckAsync(CancellationToken cancellationToken = default);
}
