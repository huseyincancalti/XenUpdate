namespace XenUpdate.Core.Enums;

/// <summary>The kind of manual, guided update a <see cref="Models.GuideItem"/> describes.</summary>
public enum GuideCategory
{
    /// <summary>Discrete or integrated GPU driver from the vendor (NVIDIA/AMD/Intel).</summary>
    GraphicsDriver,

    /// <summary>Motherboard/system BIOS or UEFI firmware.</summary>
    Bios,

    /// <summary>Other device firmware (SSD, peripherals, etc.).</summary>
    Firmware,

    /// <summary>Chipset or platform driver package.</summary>
    Chipset,

    /// <summary>
    /// Software whose own updates aren't managed by winget or Windows Update — e.g. an IDE's
    /// workload/component updates that only run through the vendor's own installer.
    /// </summary>
    Software,

    /// <summary>Anything that does not fit the categories above.</summary>
    Other
}
