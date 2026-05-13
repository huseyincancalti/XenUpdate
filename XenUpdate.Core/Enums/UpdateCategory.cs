namespace XenUpdate.Core.Enums;

/// <summary>
/// Classifies an update item into one of the four top-level categories
/// shown in the XenUpdate navigation sidebar.
/// </summary>
public enum UpdateCategory
{
    /// <summary>Installed applications updated via the Winget package manager.</summary>
    Apps,

    /// <summary>Windows OS patches, security fixes, and cumulative updates (non-driver).</summary>
    System,

    /// <summary>Hardware driver updates delivered through Windows Update.</summary>
    Drivers,

    /// <summary>Firmware, BIOS, and other hardware-component updates.</summary>
    HardwareHub
}
