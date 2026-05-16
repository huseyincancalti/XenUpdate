using System.Management;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Hardware;

/// <summary>
/// Uses WMI to query CPU and GPU information from the local machine.
/// </summary>
public sealed class HardwareScannerService : IHardwareScannerService
{
    /// <inheritdoc />
    public Task<HardwareProfile> GetCurrentHardwareAsync()
    {
        return Task.Run(() =>
        {
            var gpuName = QueryFirstString("SELECT Name FROM Win32_VideoController", "Name");
            var cpuName = QueryFirstString("SELECT Name FROM Win32_Processor", "Name");
            var gpuVendor = ParseGpuVendor(gpuName);

            return new HardwareProfile
            {
                GpuName = gpuName,
                GpuVendor = gpuVendor,
                CpuName = cpuName
            };
        });
    }

    private static string QueryFirstString(string wqlQuery, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wqlQuery);
            using var results = searcher.Get();

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

        if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("ATI", StringComparison.OrdinalIgnoreCase))
            return "AMD";

        if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";

        return "Unknown";
    }
}
