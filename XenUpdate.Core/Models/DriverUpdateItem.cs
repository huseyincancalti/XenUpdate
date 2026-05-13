using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// Represents a hardware driver update discovered via Windows Update (WUApiLib).
/// </summary>
public sealed class DriverUpdateItem : UpdateItem
{
    /// <summary>
    /// The hardware manufacturer name.
    /// Example: "NVIDIA Corporation", "Intel Corporation"
    /// </summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>
    /// The device class this driver belongs to.
    /// Example: "Display", "Network", "Audio"
    /// </summary>
    public string DeviceClass { get; init; } = string.Empty;

    /// <summary>
    /// Initializes a new <see cref="DriverUpdateItem"/> with the source preset to Driver.
    /// </summary>
    public DriverUpdateItem()
    {
        Source = UpdateSource.Driver;
    }
}
