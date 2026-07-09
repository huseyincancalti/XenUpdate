using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// One update marked to install itself the moment the app detects it is online, with no
/// manual click. Identified by its source page plus the update's own <see cref="UpdateItem.Id"/>
/// (winget package ID, KB article number, driver ID, or pip package name) — IDs are only
/// unique within a single source, so both fields together form the real key.
/// </summary>
public sealed class WhitelistEntry
{
    /// <summary>Which source page this entry belongs to.</summary>
    public UpdateSource Source { get; init; }

    /// <summary>The update's own ID within that source (winget package ID, KB number, driver ID, or pip package name).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name captured when the entry was added, shown in the Settings list.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
