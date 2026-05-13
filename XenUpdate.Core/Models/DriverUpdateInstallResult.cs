namespace XenUpdate.Core.Models;

/// <summary>
/// Represents the outcome of installing a single driver update.
/// </summary>
public sealed class DriverUpdateInstallResult
{
    /// <summary>True when the driver installation completed successfully.</summary>
    public bool Succeeded { get; init; }

    /// <summary>True when Windows Update reports that a reboot may be required.</summary>
    public bool RebootRequired { get; init; }

    /// <summary>The raw Windows Update result code returned by WUA.</summary>
    public int ResultCode { get; init; }

    /// <summary>Optional short detail text that can be logged for troubleshooting.</summary>
    public string DetailMessage { get; init; } = string.Empty;
}
