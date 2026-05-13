namespace XenUpdate.Core.Models;

/// <summary>
/// Represents one blacklist entry used to exclude a winget package from scan results.
/// </summary>
public sealed class BlacklistEntry
{
    /// <summary>
    /// The winget package ID to exclude from scan results.
    /// </summary>
    public string PackageId { get; init; } = string.Empty;

    /// <summary>
    /// Optional user-provided reason explaining why the package was blacklisted.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}
