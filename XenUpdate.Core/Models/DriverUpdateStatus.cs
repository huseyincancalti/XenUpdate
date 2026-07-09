namespace XenUpdate.Core.Models;

/// <summary>
/// Result of checking whether a newer version of something is available — a GPU driver or,
/// e.g., a Visual Studio release. <see cref="Checked"/> is false when availability could not be
/// determined (e.g. the vendor lookup failed) — callers should then fall back to a manual
/// "open the tool / page" guide rather than asserting anything.
/// </summary>
public sealed class DriverUpdateStatus
{
    /// <summary>True only when we could reliably determine the latest version.</summary>
    public bool Checked { get; init; }

    /// <summary>The installed version (e.g. "610.62" or "17.12.35527.113"), if known.</summary>
    public string InstalledVersion { get; init; } = string.Empty;

    /// <summary>The latest available version, if determined.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>True when <see cref="LatestVersion"/> is newer than <see cref="InstalledVersion"/>.</summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>Direct download URL for the latest version, if available.</summary>
    public string? DownloadUrl { get; init; }
}
