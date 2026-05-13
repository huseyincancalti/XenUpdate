namespace XenUpdate.Core.Enums;

/// <summary>
/// Represents the lifecycle state of a single update item as it moves
/// through discovery, filtering, and installation.
/// </summary>
public enum UpdateStatus
{
    /// <summary>The update has been discovered and is waiting for user action.</summary>
    Pending,

    /// <summary>The update is excluded by the user's blacklist. Not shown in the main update list.</summary>
    Blacklisted,

    /// <summary>The update is currently being downloaded.</summary>
    Downloading,

    /// <summary>The update is currently being installed.</summary>
    Installing,

    /// <summary>The update was installed successfully.</summary>
    Succeeded,

    /// <summary>The update failed to install. Check the log for details.</summary>
    Failed
}
