using System.Management;
using System.Text.Json;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Hardware;

/// <summary>
/// Determines whether a newer NVIDIA driver is available. Reads the installed version from WMI
/// and queries NVIDIA's (unofficial) GfeClientContent driver-lookup endpoint with the GPU's PCI
/// id. Everything is wrapped so any failure returns <c>Checked=false</c> — the UI then falls back
/// to a manual guide instead of showing wrong information.
/// </summary>
public sealed class NvidiaDriverService : INvidiaDriverService
{
    private const string Endpoint =
        "https://gfwsl.geforce.com/nvidia_web_services/controller.gfeclientcontent.NG.php/com.nvidia.services.GFEClientContent_NG.getDispDrvrByDevid";

    private static readonly HttpClient Http = CreateClient();
    private static readonly int[] LaptopChassisTypes = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };

    private readonly ILoggerService _logger;

    /// <summary>Initializes a new <see cref="NvidiaDriverService"/>.</summary>
    public NvidiaDriverService(ILoggerService logger) => _logger = logger;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NvBackend/36.0.0.0");
        return client;
    }

    /// <inheritdoc />
    public async Task<DriverUpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var gpu = await Task.Run(QueryNvidiaGpu, cancellationToken);
            if (gpu is null)
                return new DriverUpdateStatus { Checked = false };

            var (wmiVersion, pnpId) = gpu.Value;
            var installed = ToMarketingVersion(wmiVersion);
            var ven = ExtractHex(pnpId, "VEN_");
            var dev = ExtractHex(pnpId, "DEV_");
            if (installed is null || ven is null || dev is null)
                return new DriverUpdateStatus { Checked = false, InstalledVersion = installed ?? string.Empty };

            var url = $"{Endpoint}/{BuildQuery(dev, ven, await Task.Run(IsLaptop, cancellationToken))}";

            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new DriverUpdateStatus { Checked = false, InstalledVersion = installed };

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var (latest, downloadUrl) = ParseLatest(json);
            if (latest is null)
                return new DriverUpdateStatus { Checked = false, InstalledVersion = installed };

            var updateAvailable = IsNewer(latest, installed);
            _logger.Info($"NVIDIA driver check: installed {installed}, latest {latest}, updateAvailable={updateAvailable}.");

            return new DriverUpdateStatus
            {
                Checked = true,
                InstalledVersion = installed,
                LatestVersion = latest,
                UpdateAvailable = updateAvailable,
                DownloadUrl = downloadUrl
            };
        }
        catch (Exception ex)
        {
            _logger.Info($"NVIDIA driver check failed (non-fatal): {ex.Message}");
            return new DriverUpdateStatus { Checked = false };
        }
    }

    private static (string Version, string Pnp)? QueryNvidiaGpu()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DriverVersion, PNPDeviceID FROM Win32_VideoController");

        foreach (ManagementObject mo in searcher.Get())
        {
            var pnp = mo["PNPDeviceID"]?.ToString() ?? string.Empty;
            if (pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase))
                return (mo["DriverVersion"]?.ToString() ?? string.Empty, pnp);
        }

        return null;
    }

    // WMI reports e.g. "32.0.16.1062"; NVIDIA's marketing version is the last 5 digits → "610.62".
    private static string? ToMarketingVersion(string wmiVersion)
    {
        var digits = new string(wmiVersion.Where(char.IsDigit).ToArray());
        if (digits.Length < 5)
            return null;

        var last5 = digits[^5..];
        return $"{last5[..3]}.{last5[3..]}";
    }

    private static string? ExtractHex(string pnp, string prefix)
    {
        var idx = pnp.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || idx + prefix.Length + 4 > pnp.Length)
            return null;

        return pnp.Substring(idx + prefix.Length, 4).ToUpperInvariant();
    }

    private static bool IsLaptop()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["ChassisTypes"] is ushort[] types && types.Any(t => LaptopChassisTypes.Contains(t)))
                    return true;
            }
        }
        catch
        {
            // Default to desktop if chassis info is unavailable.
        }

        return false;
    }

    private static string BuildQuery(string dev, string ven, bool isLaptop)
    {
        var os = Environment.OSVersion.Version;
        var query = new Dictionary<string, object>
        {
            ["dIDa"] = new[] { $"{dev}_{ven}" },
            ["osC"] = $"{os.Major}.{os.Minor}",
            ["osB"] = os.Build.ToString(),
            ["is6"] = "1",
            ["lg"] = "1033",
            ["iLp"] = isLaptop ? "1" : "0",
            ["dch"] = "1",
            ["isCRD"] = "0",
            ["upCRD"] = "0"
        };
        return JsonSerializer.Serialize(query);
    }

    private static (string? Version, string? DownloadUrl) ParseLatest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var attrs = FindDriverAttributes(doc.RootElement);
        if (attrs is null)
            return (null, null);

        var version = attrs.Value.TryGetProperty("Version", out var v) ? v.GetString() : null;
        var url = attrs.Value.TryGetProperty("DownloadURLAdmin", out var d) ? d.GetString() : null;
        return (version, url);
    }

    private static JsonElement? FindDriverAttributes(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("DriverAttributes", out var da) && da.ValueKind == JsonValueKind.Object)
                    return da;
                foreach (var prop in element.EnumerateObject())
                {
                    var found = FindDriverAttributes(prop.Value);
                    if (found is not null)
                        return found;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindDriverAttributes(item);
                    if (found is not null)
                        return found;
                }
                break;
        }

        return null;
    }

    private static bool IsNewer(string latest, string installed)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(installed, out var i))
            return l > i;

        return !string.Equals(latest, installed, StringComparison.OrdinalIgnoreCase);
    }
}
