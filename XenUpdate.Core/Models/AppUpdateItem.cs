using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// Represents a third-party application update discovered by winget.
/// </summary>
public sealed class AppUpdateItem : UpdateItem
{
    /// <summary>
    /// The winget package ID used to install the update.
    /// Example: "Google.Chrome", "Microsoft.VisualStudioCode"
    /// </summary>
    public string WingetPackageId { get; init; } = string.Empty;

    /// <summary>The publisher or developer of the application, as reported by winget.</summary>
    public string Publisher { get; init; } = string.Empty;

    /// <summary>
    /// Initializes a new <see cref="AppUpdateItem"/> with the source preset to Winget.
    /// </summary>
    public AppUpdateItem()
    {
        Source = UpdateSource.Winget;
    }
}
