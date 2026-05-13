namespace XenUpdate.Core.Enums;

/// <summary>
/// Indicates the severity of a log entry.
/// Used by the UI log console to color-code messages.
/// </summary>
public enum LogSeverity
{
    /// <summary>Routine informational message.</summary>
    Info,

    /// <summary>Something unexpected happened, but the operation can continue.</summary>
    Warning,

    /// <summary>An operation failed. The user should check the log file for details.</summary>
    Error
}
