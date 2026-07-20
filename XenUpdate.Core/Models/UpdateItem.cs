using CommunityToolkit.Mvvm.ComponentModel;
using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// Abstract base class for all update items, regardless of source.
/// Subclass this for winget apps, Windows Updates, and drivers.
/// </summary>
public abstract partial class UpdateItem : ObservableObject
{
    /// <summary>
    /// Unique identifier for this update item.
    /// For winget: the package ID (e.g. "Microsoft.VisualStudioCode").
    /// For Windows Update: the KB article number.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name shown in the UI.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The version currently installed on this machine.</summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>The newer version available to install.</summary>
    public string AvailableVersion { get; init; } = string.Empty;

    /// <summary>Which system reported this update (Winget, WindowsUpdate, Driver).</summary>
    public UpdateSource Source { get; init; }

    /// <summary>
    /// Current lifecycle state of this update.
    /// Changes as the user interacts with it (Pending -> Installing -> Succeeded).
    /// </summary>
    [ObservableProperty]
    private UpdateStatus _status = UpdateStatus.Pending;

    /// <summary>Whether the user has selected this item for batch updating.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Whether this update is on the whitelist: pre-approved to install itself the moment
    /// the app detects it is online, with no manual click. Refreshed after every scan.
    /// </summary>
    [ObservableProperty]
    private bool _isWhitelisted;

    /// <summary>
    /// Human-readable reason when this item's install failed.
    /// Null (default) on items that have not failed; shown as a tooltip on the "Failed" badge.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 0-100 install progress while <see cref="Status"/> is Installing. Reset to 0 when a new
    /// install starts; meaningless (and ignored by the UI) in any other status. Lets any live
    /// view of an in-progress batch (e.g. the update queue window) show a real progress bar per
    /// item without needing to know which page-specific service is installing it.
    /// </summary>
    [ObservableProperty]
    private int _progressPercent;
}
