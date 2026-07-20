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

    private bool _autoCheckEnabled;

    /// <summary>
    /// If true, XenUpdate can automatically check for updates based on the user's preference.
    /// </summary>
    public bool AutoCheckEnabled
    {
        get => _autoCheckEnabled;
        set => SetField(ref _autoCheckEnabled, value);
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

    private string _language = string.Empty;

    /// <summary>
    /// The UI language code (e.g. "en", "tr"). Empty means "not chosen yet" — the app
    /// auto-detects from the OS on first run.
    /// </summary>
    public string Language
    {
        get => _language;
        set => SetField(ref _language, value);
    }

    private bool _runOnStartup;

    /// <summary>
    /// If true, XenUpdate registers itself in the Windows Run registry key so it
    /// launches automatically (hidden, using <c>-background</c>) when Windows starts.
    /// Default: false.
    /// </summary>
    public bool RunOnStartup
    {
        get => _runOnStartup;
        set => SetField(ref _runOnStartup, value);
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

    private string _accentColorHex = "#7C3AED";

    /// <summary>
    /// The user's chosen primary/accent color, as a "#RRGGBB" hex string. Drives ZenPrimaryBrush
    /// and the other accent-tied Zen* brushes app-wide, plus MaterialDesignThemes' own palette.
    /// Default matches the app's original built-in violet (the logo's color).
    /// </summary>
    public string AccentColorHex
    {
        get => _accentColorHex;
        set => SetField(ref _accentColorHex, value);
    }

    private string? _secondaryColorHex;

    /// <summary>
    /// The user's chosen secondary color, as a "#RRGGBB" hex string. Null (default) means "no
    /// explicit secondary" — ThemeService derives a lighter variant of the primary color instead,
    /// which is exactly what happened before this setting existed, so leaving it unset never
    /// looks broken.
    /// </summary>
    public string? SecondaryColorHex
    {
        get => _secondaryColorHex;
        set => SetField(ref _secondaryColorHex, value);
    }

    private string _backgroundColorHex = "#1C1C24";

    /// <summary>
    /// The user's chosen base background color, as a "#RRGGBB" hex string. Drives every
    /// background-tied Zen* brush app-wide (surfaces, sidebar, dividers, glass panels, the log
    /// drawer/update queue windows' chrome) via lighten/darken derivation, so the whole app
    /// — not just the accent — follows one consistent palette. Default is a neutral dark grey
    /// rather than the app's original purple-tinted background: with an accent color that's
    /// also purple by default, a purple background made the accent blend in instead of standing
    /// out against it.
    /// </summary>
    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set => SetField(ref _backgroundColorHex, value);
    }

    private string? _backgroundImagePath;

    /// <summary>
    /// Absolute path to a user-chosen background photo, shown blurred behind the app's content.
    /// Null (default) means no custom photo — the app falls back to its built-in ambient gradient.
    /// </summary>
    public string? BackgroundImagePath
    {
        get => _backgroundImagePath;
        set => SetField(ref _backgroundImagePath, value);
    }

    private double _backgroundBlurRadius = 12;

    /// <summary>
    /// Blur strength applied to <see cref="BackgroundImagePath"/>, in WPF BlurEffect radius
    /// units (roughly 0-80 is a sane range). Meaningless when no background photo is set.
    /// </summary>
    public double BackgroundBlurRadius
    {
        get => _backgroundBlurRadius;
        set => SetField(ref _backgroundBlurRadius, value);
    }

    private bool _spotlightEnabled;

    /// <summary>
    /// If true, a soft accent-colored glow follows the mouse cursor over the app's glass
    /// panels. Purely decorative and off by default — an explicit opt-in rather than a
    /// surprise a first-time user has to go turn off.
    /// </summary>
    public bool SpotlightEnabled
    {
        get => _spotlightEnabled;
        set => SetField(ref _spotlightEnabled, value);
    }

    /// <summary>
    /// Single colors ("#RRGGBB") the user chose to keep, shown as reusable swatches inside the
    /// color picker. Unlimited and local-only. Plain list (no change notification): the
    /// Settings page mirrors it into its own observable collection.
    /// </summary>
    public List<string> SavedCustomColors { get; set; } = new();

    /// <summary>
    /// Full Primary/Secondary/Background themes the user saved from the Appearance settings,
    /// rendered after the built-in palette presets. Unlimited and local-only.
    /// </summary>
    public List<SavedPalette> SavedCustomPalettes { get; set; } = new();

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
