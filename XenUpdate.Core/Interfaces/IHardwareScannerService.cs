using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Provides hardware information for the local machine via WMI.
/// </summary>
public interface IHardwareScannerService
{
    /// <summary>
    /// Queries WMI for CPU and GPU information and returns a populated <see cref="HardwareProfile"/>.
    /// </summary>
    Task<HardwareProfile> GetCurrentHardwareAsync();
}
