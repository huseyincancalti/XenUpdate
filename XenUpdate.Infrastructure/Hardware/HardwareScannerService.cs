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
            var gpuNames = QueryAllStrings("SELECT Name FROM Win32_VideoController", "Name");
            var gpuName = PickPrimaryGpu(gpuNames);
            var cpuName = QueryFirstString("SELECT Name FROM Win32_Processor", "Name");

            return new HardwareProfile
            {
                GpuName = gpuName,
                GpuVendor = ParseGpuVendor(gpuName),
                CpuName = cpuName
            };
        });
    }

    /// <summary>
    /// Picks the GPU that guided driver updates should target. Hybrid-graphics laptops report
    /// several video controllers (e.g. an Intel iGPU plus an NVIDIA/AMD dGPU) and the WMI order
    /// is not guaranteed, so prefer a discrete vendor over the integrated one. Public for testing.
    /// </summary>
    public static string PickPrimaryGpu(IReadOnlyList<string> gpuNames)
    {
        if (gpuNames.Count == 0)
            return string.Empty;

        foreach (var name in gpuNames)
        {
            if (ParseGpuVendor(name) is "NVIDIA" or "AMD")
                return name;
        }

        return gpuNames[0];
    }

    /// <summary>Parses the GPU vendor ("NVIDIA", "AMD", "Intel", or "Unknown") from a controller name. Public for testing.</summary>
    public static string ParseGpuVendor(string gpuName)
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

    private static List<string> QueryAllStrings(string wqlQuery, string propertyName)
    {
        var values = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wqlQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                var value = obj[propertyName]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }
        }
        catch (ManagementException)
        {
        }

        return values;
    }

    private static string QueryFirstString(string wqlQuery, string propertyName)
    {
        var all = QueryAllStrings(wqlQuery, propertyName);
        return all.Count > 0 ? all[0] : string.Empty;
    }
}
