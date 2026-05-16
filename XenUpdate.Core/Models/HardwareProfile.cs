namespace XenUpdate.Core.Models;

/// <summary>
/// Snapshot of the local machine's relevant hardware, resolved once at startup.
/// </summary>
public sealed record HardwareProfile
{
    /// <summary>Full GPU name as reported by Win32_VideoController (e.g. "NVIDIA GeForce RTX 4070").</summary>
    public string GpuName { get; init; } = string.Empty;

    /// <summary>Parsed GPU vendor: "NVIDIA", "AMD", "Intel", or "Unknown".</summary>
    public string GpuVendor { get; init; } = string.Empty;

    /// <summary>Full CPU name as reported by Win32_Processor (e.g. "Intel(R) Core(TM) i7-13700K").</summary>
    public string CpuName { get; init; } = string.Empty;
}
