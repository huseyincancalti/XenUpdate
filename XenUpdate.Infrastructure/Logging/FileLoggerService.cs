using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Logging;

/// <summary>
/// Writes log entries to a daily rolling log file and raises <see cref="LogEntryAdded"/>
/// so the UI log console can display entries in real time.
/// Implements <see cref="ILoggerService"/>.
/// Log files are stored at <c>%APPDATA%\XenUpdate\logs\XenUpdate-yyyy-MM-dd.log</c>.
/// </summary>
public sealed class FileLoggerService : ILoggerService
{
    private readonly string _logDirectory;

    // Thread-safe file writing lock.
    private readonly object _writeLock = new();

    /// <inheritdoc />
    /// <remarks>
    /// This event can fire from a background thread.
    /// Any UI subscriber (e.g., LogConsoleViewModel) MUST dispatch to the UI thread
    /// before touching any WPF controls or ObservableCollection.
    /// </remarks>
    public event Action<LogEntry>? LogEntryAdded;

    /// <summary>
    /// Initializes a new <see cref="FileLoggerService"/> using the default log directory.
    /// </summary>
    public FileLoggerService()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XenUpdate", "logs");
    }

    /// <inheritdoc />
    public void Info(string message) => Write(LogSeverity.Info, message);

    /// <inheritdoc />
    public void Warning(string message) => Write(LogSeverity.Warning, message);

    /// <inheritdoc />
    public void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex is null ? message : $"{message} | Exception: {ex.Message}";
        Write(LogSeverity.Error, fullMessage);
    }

    /// <summary>
    /// Creates a <see cref="LogEntry"/>, writes it to the log file, and raises the event.
    /// </summary>
    private void Write(LogSeverity severity, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Severity = severity,
            Message = message
        };

        WriteToFile(entry);

        // Notify the UI. The subscriber is responsible for thread-switching.
        LogEntryAdded?.Invoke(entry);
    }

    /// <summary>
    /// Writes a log entry to the daily log file.
    /// Uses a lock to prevent file access conflicts in multithreaded scenarios.
    /// </summary>
    private void WriteToFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var fileName = $"XenUpdate-{DateTime.Now:yyyy-MM-dd}.log";
            var filePath = Path.Combine(_logDirectory, fileName);

            lock (_writeLock)
            {
                File.AppendAllText(filePath, entry.ToString() + Environment.NewLine);
            }
        }
        catch
        {
            // Swallow file write errors silently.
            // The logger must never crash the application.
        }
    }
}
