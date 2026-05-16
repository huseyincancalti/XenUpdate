using System.Management;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Hardware;

/// <summary>
/// Uses WMI to interrogate the local hardware and build a <see cref="HardwareProfile"/>.
/// All WMI calls are synchronous; call from a background thread if startup latency matters.
/// </summary>
public static class HardwareDetector
{
    /// <summary>
    /// Queries <c>Win32_VideoController</c> and <c>Win32_Processor</c> via WMI
    /// and returns a populated <see cref="HardwareProfile"/>.
    /// Returns empty strings for any field that cannot be resolved.
    /// </summary>
    public static HardwareProfile GetSystemHardware()
    {
        var gpuName   = QueryFirstString("SELECT Name FROM Win32_VideoController", "Name");
        var cpuName   = QueryFirstString("SELECT Name FROM Win32_Processor",       "Name");
        var gpuVendor = ParseGpuVendor(gpuName);

        return new HardwareProfile
        {
            GpuName   = gpuName,
            GpuVendor = gpuVendor,
            CpuName   = cpuName
        };
    }

    private static string QueryFirstString(string wqlQuery, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wqlQuery);
            using var results  = searcher.Get();

            foreach (ManagementObject obj in results)
                return obj[propertyName]?.ToString()?.Trim() ?? string.Empty;
        }
        catch (ManagementException)
        {
        }

        return string.Empty;
    }

    private static string ParseGpuVendor(string gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
            return "Unknown";

        if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";

        if (gpuName.Contains("AMD",    StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("ATI",    StringComparison.OrdinalIgnoreCase))
            return "AMD";

        if (gpuName.Contains("Intel",  StringComparison.OrdinalIgnoreCase))
            return "Intel";

        return "Unknown";
    }
}
