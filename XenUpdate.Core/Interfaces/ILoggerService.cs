using XenUpdate.Core.Models;

namespace XenUpdate.Core.Interfaces;

/// <summary>
/// Writes application log messages to a file and notifies the UI via an event.
/// Inject this service into any class that needs to log.
/// </summary>
public interface ILoggerService
{
    /// <summary>
    /// Fired whenever a new log entry is created.
    /// The UI log console subscribes to this event to display entries in real time.
    /// Note: This event may be raised from a background thread. UI subscribers must dispatch accordingly.
    /// </summary>
    event Action<LogEntry> LogEntryAdded;

    /// <summary>Logs a routine informational message.</summary>
    /// <param name="message">The message to log.</param>
    void Info(string message);

    /// <summary>Logs a warning — something unexpected happened but the app can continue.</summary>
    /// <param name="message">The message to log.</param>
    void Warning(string message);

    /// <summary>
    /// Logs an error — an operation has failed.
    /// Optionally includes the exception details.
    /// </summary>
    /// <param name="message">A description of what failed.</param>
    /// <param name="ex">The exception that caused the failure, if available.</param>
    void Error(string message, Exception? ex = null);
}
