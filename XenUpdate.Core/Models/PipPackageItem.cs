using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// Represents an outdated Python package discovered via <c>pip list --outdated</c>.
/// </summary>
public sealed class PipPackageItem : UpdateItem
{
    /// <summary>
    /// The exact package name used with <c>pip install --upgrade</c>.
    /// Equal to <see cref="UpdateItem.DisplayName"/> — pip has no separate
    /// friendly-name concept the way winget does.
    /// </summary>
    public string PackageName { get; init; } = string.Empty;

    /// <summary>
    /// Initializes a new <see cref="PipPackageItem"/> with the source preset to Pip.
    /// </summary>
    public PipPackageItem()
    {
        Source = UpdateSource.Pip;
    }
}
