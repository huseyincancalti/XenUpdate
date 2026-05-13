using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// Represents a single entry in the application log.
/// Displayed in the UI log console and written to the log file.
/// </summary>
public sealed class LogEntry
{
    /// <summary>The exact date and time this log entry was created.</summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>The severity of this message (Info, Warning, Error).</summary>
    public LogSeverity Severity { get; init; }

    /// <summary>The human-readable log message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Returns a formatted string representation suitable for writing to a log file.
    /// Example: "[2026-04-19 22:15:00] [INFO] Scan started."
    /// </summary>
    public override string ToString()
        => $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Severity.ToString().ToUpperInvariant()}] {Message}";
}
