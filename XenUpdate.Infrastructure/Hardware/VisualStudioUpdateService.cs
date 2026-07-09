using System.Text.Json;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Winget;

namespace XenUpdate.Infrastructure.Hardware;

/// <summary>
/// Determines whether a newer Visual Studio release is available for the installed edition.
/// Reads the installed version and update channel via vswhere.exe (bundled with every Visual
/// Studio installation under the Installer folder), then compares against Microsoft's public
/// channel manifest for that channel — the same manifest the Visual Studio Installer itself
/// consults. Everything is wrapped so any failure returns <c>Checked=false</c>: the guide then
/// falls back to always showing its step-by-step instructions instead of asserting wrong
/// information, the same fail-soft contract <see cref="NvidiaDriverService"/> follows.
/// </summary>
public sealed class VisualStudioUpdateService : IVisualStudioUpdateService
{
    private static readonly string[] VswhereCandidates =
    {
        @"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe",
        @"%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly ProcessRunner _processRunner;
    private readonly ILoggerService _logger;

    /// <summary>Initializes a new <see cref="VisualStudioUpdateService"/>.</summary>
    public VisualStudioUpdateService(ProcessRunner processRunner, ILoggerService logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DriverUpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var vswhere = VswhereCandidates
                .Select(Environment.ExpandEnvironmentVariables)
                .FirstOrDefault(File.Exists);

            if (vswhere is null)
                return new DriverUpdateStatus { Checked = false };

            var result = await _processRunner.RunAsync(vswhere, "-all -format json -utf8", cancellationToken);
            if (result.ExitCode != 0)
                return new DriverUpdateStatus { Checked = false };

            var instance = ParseInstalledInstance(result.StandardOutput);
            if (instance is null)
                return new DriverUpdateStatus { Checked = false };

            var (productId, channelUri, installedVersion) = instance.Value;
            if (string.IsNullOrWhiteSpace(channelUri))
                return new DriverUpdateStatus { Checked = false, InstalledVersion = installedVersion };

            using var response = await Http.GetAsync(channelUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new DriverUpdateStatus { Checked = false, InstalledVersion = installedVersion };

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var latest = FindLatestVersion(json, productId);
            if (latest is null)
                return new DriverUpdateStatus { Checked = false, InstalledVersion = installedVersion };

            var updateAvailable = IsNewer(latest, installedVersion);
            _logger.Info($"Visual Studio update check: installed {installedVersion}, latest {latest}, updateAvailable={updateAvailable}.");

            return new DriverUpdateStatus
            {
                Checked = true,
                InstalledVersion = installedVersion,
                LatestVersion = latest,
                UpdateAvailable = updateAvailable
            };
        }
        catch (Exception ex)
        {
            _logger.Info($"Visual Studio update check failed (non-fatal): {ex.Message}");
            return new DriverUpdateStatus { Checked = false };
        }
    }

    // vswhere -all -format json returns an array of installed instances. We only look at the
    // first one — multi-instance (side-by-side VS editions) is a rare enough setup that picking
    // the first is a reasonable v1 simplification, matching how the guide only launches one.
    internal static (string ProductId, string ChannelUri, string InstalledVersion)? ParseInstalledInstance(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return null;

        var first = doc.RootElement[0];
        var productId = first.TryGetProperty("productId", out var p) ? p.GetString() : null;
        var channelUri = first.TryGetProperty("channelUri", out var c) ? c.GetString() : null;
        var version = first.TryGetProperty("installationVersion", out var v) ? v.GetString() : null;

        if (string.IsNullOrEmpty(productId) || string.IsNullOrEmpty(version))
            return null;

        return (productId, channelUri ?? string.Empty, version);
    }

    // The channel manifest's exact nesting isn't a stable contract we want to hardcode a fixed
    // path against, so — like NvidiaDriverService.FindDriverAttributes — we search the whole
    // document for an object carrying both the matching "id" and a "version" field.
    internal static string? FindLatestVersion(string json, string productId)
    {
        using var doc = JsonDocument.Parse(json);
        return FindVersionForId(doc.RootElement, productId);
    }

    private static string? FindVersionForId(JsonElement element, string productId)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("id", out var idProp) &&
                    string.Equals(idProp.GetString(), productId, StringComparison.OrdinalIgnoreCase) &&
                    element.TryGetProperty("version", out var versionProp))
                {
                    return versionProp.GetString();
                }

                foreach (var prop in element.EnumerateObject())
                {
                    var found = FindVersionForId(prop.Value, productId);
                    if (found is not null)
                        return found;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindVersionForId(item, productId);
                    if (found is not null)
                        return found;
                }
                break;
        }

        return null;
    }

    internal static bool IsNewer(string latest, string installed)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(installed, out var i))
            return l > i;

        return !string.Equals(latest, installed, StringComparison.OrdinalIgnoreCase);
    }
}
