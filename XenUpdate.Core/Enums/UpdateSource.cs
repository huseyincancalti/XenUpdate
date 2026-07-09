namespace XenUpdate.Core.Enums;

/// <summary>
/// Identifies which system or package manager reported an update.
/// Used to distinguish winget app updates from OS/driver updates.
/// </summary>
public enum UpdateSource
{
    /// <summary>Update discovered by the winget package manager.</summary>
    Winget,

    /// <summary>Update discovered by the Windows Update service (OS patches, security fixes).</summary>
    WindowsUpdate,

    /// <summary>Update discovered by the Windows Update service (hardware drivers).</summary>
    Driver,

    /// <summary>Update discovered by pip (outdated Python packages).</summary>
    Pip
}
