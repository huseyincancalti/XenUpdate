using CommunityToolkit.Mvvm.ComponentModel;
using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// A concrete, unified update item that carries a <see cref="UpdateCategory"/>
/// and an optional download size alongside the base update fields.
/// Use this for cross-source aggregation (e.g., the "All Updates" view).
/// </summary>
public partial class CategorizedUpdateItem : ObservableObject
{
    /// <summary>
    /// Unique identifier.
    /// Winget: package ID (e.g. "Microsoft.VisualStudioCode").
    /// Windows Update: KB article number (e.g. "KB5034441").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name shown in the UI.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The version currently installed on this machine.</summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>The newer version available to install.</summary>
    public string NewVersion { get; init; } = string.Empty;

    /// <summary>Download size in bytes; <see langword="null"/> when unknown.</summary>
    public ulong? DownloadSizeBytes { get; init; }

    /// <summary>
    /// Human-readable download size.
    /// Returns "Unknown" when <see cref="DownloadSizeBytes"/> is <see langword="null"/>.
    /// </summary>
    public string DisplaySize => DownloadSizeBytes switch
    {
        null => "Unknown",
        ulong b when b >= 1_073_741_824 => $"{b / 1_073_741_824.0:F1} GB",
        ulong b when b >= 1_048_576 => $"{b / 1_048_576.0:F1} MB",
        ulong b when b >= 1_024 => $"{b / 1_024.0:F1} KB",
        ulong b => $"{b} B"
    };

    /// <summary>Current lifecycle state of this update.</summary>
    [ObservableProperty]
    private UpdateStatus _status = UpdateStatus.Pending;

    /// <summary>Top-level category used for sidebar navigation filtering.</summary>
    [ObservableProperty]
    private UpdateCategory _category;

    /// <summary>Whether the user has selected this item for batch updating.</summary>
    [ObservableProperty]
    private bool _isSelected;
}
