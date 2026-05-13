using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// Represents a Windows OS update discovered via Windows Update (WUApiLib).
/// Examples: security patches, cumulative updates, .NET runtime updates.
/// </summary>
public sealed class WindowsUpdateItem : UpdateItem
{
    /// <summary>
    /// The KB (Knowledge Base) article ID for this update.
    /// Example: "KB5035942"
    /// </summary>
    public string KbArticleId { get; init; } = string.Empty;

    /// <summary>
    /// Whether Windows Update classifies this as an important/critical update.
    /// Important updates should be highlighted in the UI.
    /// </summary>
    public bool IsImportant { get; init; }

    /// <summary>
    /// The raw MSRC (Microsoft Security Response Center) severity string from Windows Update.
    /// Common values: "Critical", "Important", "Moderate", "Low", or empty for unclassified updates.
    /// </summary>
    public string MsrcSeverity { get; init; } = string.Empty;

    /// <summary>
    /// Initializes a new <see cref="WindowsUpdateItem"/> with the source preset to WindowsUpdate.
    /// </summary>
    public WindowsUpdateItem()
    {
        Source = UpdateSource.WindowsUpdate;
    }
}
