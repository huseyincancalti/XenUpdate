using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace XenUpdate.App.Services;

/// <summary>
/// Lightweight in-process logger. Appends entries to an observable collection
/// (for the Log Viewer UI) and optionally to <c>%APPDATA%\XenUpdate\logs.txt</c>.
/// Thread-safe: writes are marshalled to the UI thread via the current Dispatcher.
/// </summary>
public static class AppLogger
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XenUpdate", "logs.txt");

    /// <summary>
    /// In-memory ring of log entries bound to the Log Viewer window.
    /// Must be accessed on the UI thread.
    /// </summary>
    public static ObservableCollection<string> Logs { get; } = new();

    /// <summary>
    /// Appends a timestamped <paramref name="message"/> to <see cref="Logs"/>
    /// and writes it to the log file on disk.
    /// </summary>
    public static void AddLog(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => Logs.Add(entry));
        }
        else
        {
            Logs.Add(entry);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            File.AppendAllText(LogFilePath, entry + Environment.NewLine);
        }
        catch
        {
            // File write is best-effort; never crash the caller.
        }
    }

    /// <summary>Returns all log entries joined into a single string.</summary>
    public static string GetAll() => string.Join(Environment.NewLine, Logs);

    /// <summary>Clears the in-memory log collection.</summary>
    public static void Clear()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => Logs.Clear());
        }
        else
        {
            Logs.Clear();
        }
    }
}
