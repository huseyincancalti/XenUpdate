using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Logging;

/// <summary>
/// Writes log entries to <c>%APPDATA%\XenUpdate\logs.txt</c> and raises <see cref="LogEntryAdded"/>
/// so the UI log console can display entries in real time.
/// Implements <see cref="ILoggerService"/>.
/// </summary>
public sealed class FileLoggerService : ILoggerService
{
    private readonly string _logFilePath;

    private readonly object _writeLock = new();

    /// <inheritdoc />
    /// <remarks>
    /// This event can fire from a background thread.
    /// Any UI subscriber (e.g., LogConsoleViewModel) MUST dispatch to the UI thread
    /// before touching any WPF controls or ObservableCollection.
    /// </remarks>
    public event Action<LogEntry>? LogEntryAdded;

    /// <summary>
    /// Initializes a new <see cref="FileLoggerService"/> using the default log file path.
    /// </summary>
    public FileLoggerService()
    {
        _logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XenUpdate", "logs.txt");
    }

    /// <inheritdoc />
    public void Info(string message) => Write(LogSeverity.Info, message);

    /// <inheritdoc />
    public void Warning(string message) => Write(LogSeverity.Warning, message);

    /// <inheritdoc />
    public void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex is null
            ? message
            : $"{message}{Environment.NewLine}Exception: {ex}";
        Write(LogSeverity.Error, fullMessage);
    }

    private void Write(LogSeverity severity, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Severity = severity,
            Message = message
        };

        WriteToFile(entry);

        LogEntryAdded?.Invoke(entry);
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath)!;
            Directory.CreateDirectory(directory);

            lock (_writeLock)
            {
                File.AppendAllText(_logFilePath, entry.ToString() + Environment.NewLine);
            }
        }
        catch
        {
            // Swallow file write errors silently.
            // The logger must never crash the application.
        }
    }

    /// <summary>
    /// Returns the full path of the log file.
    /// </summary>
    public string LogFilePath => _logFilePath;
}
