using System.ComponentModel;
using System.Runtime.CompilerServices;
using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// Stores user-configurable application settings.
/// Serialized to and from <c>%APPDATA%\XenUpdate\settings.json</c>.
/// Implements <see cref="INotifyPropertyChanged"/> so the Settings page
/// can auto-save when a checkbox is toggled.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private bool _scanOnStartup;

    /// <summary>
    /// If true, XenUpdate checks for updates automatically when the app starts.
    /// Default: false (user must trigger scans manually).
    /// </summary>
    public bool ScanOnStartup
    {
        get => _scanOnStartup;
        set => SetField(ref _scanOnStartup, value);
    }

    private bool _minimizeToTray;

    /// <summary>
    /// If true, XenUpdate minimizes to the system tray instead of closing when the window is closed.
    /// Default: false.
    /// </summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetField(ref _minimizeToTray, value);
    }

    private AppTheme _theme = AppTheme.Dark;

    /// <summary>
    /// The visual theme the user has selected. Default: <see cref="AppTheme.Dark"/>.
    /// The value is applied at startup and whenever the user changes it on the Settings page.
    /// </summary>
    public AppTheme Theme
    {
        get => _theme;
        set => SetField(ref _theme, value);
    }

    /// <summary>
    /// The folder where log files are written.
    /// Default: <c>%APPDATA%\XenUpdate\logs</c>.
    /// </summary>
    public string LogDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XenUpdate", "logs");

    /// <summary>
    /// The maximum number of log entries to display in the UI log console.
    /// Older entries are removed automatically when the limit is exceeded.
    /// Default: 200.
    /// </summary>
    public int MaxLogConsoleEntries { get; set; } = 200;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
