using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using XenUpdate.App.Messages;
using XenUpdate.App.Services;
using XenUpdate.Core.Interfaces;

namespace XenUpdate.App.ViewModels;

/// <summary>The navigable pages hosted by the shell.</summary>
public enum AppPage
{
    Overview,
    Programs,
    WindowsUpdates,
    Drivers,
    PipPackages,
    HardwareHub,
    Settings
}

/// <summary>
/// Top-level ViewModel for the application shell (the main window).
/// Owns page navigation plus window-level concerns: tray actions, the app self-update
/// banner, and one-shot startup tasks. The title-bar theme toggle binds through
/// <see cref="Settings"/> so theme state has a single owner (the Settings page).
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly OverviewViewModel _overviewVm;
    private readonly ProgramsViewModel _programsVm;
    private readonly WindowsUpdatesViewModel _windowsUpdatesVm;
    private readonly DriversViewModel _driversVm;
    private readonly PipPackagesViewModel _pipPackagesVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly HardwareHubViewModel _hardwareHubVm;
    private readonly IAppUpdateService _appUpdateService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILoggerService _logger;

    private string _appUpdateUrl = string.Empty;

    private readonly NetworkMonitorService _networkMonitor = new();

    public LogConsoleViewModel LogConsole { get; }

    /// <summary>The Settings page VM. Also the single source of theme state for the title-bar toggle.</summary>
    public SettingsViewModel Settings => _settingsVm;

    /// <summary>The Guides page VM. Exposed so the sidebar can show its categories as sub-branches.</summary>
    public HardwareHubViewModel GuidesViewModel => _hardwareHubVm;

    /// <summary>The page ViewModel shown in the shell's content area.</summary>
    [ObservableProperty]
    private ObservableObject? _currentPage;

    /// <summary>The active navigation selection.</summary>
    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Overview;

    /// <summary>True when a newer XenUpdate release is available; drives the title-bar banner.</summary>
    [ObservableProperty]
    private bool _hasAppUpdate;

    /// <summary>True while a "Scan All" run is in progress; drives the sidebar button's spinner/label.</summary>
    [ObservableProperty]
    private bool _isScanningAll;

    /// <summary>True when the machine has network connectivity; false dims the UI and shows the title-bar badge.</summary>
    [ObservableProperty]
    private bool _isOnline = true;

    /// <summary>Set by the window: brings the window to the foreground (tray "Open").</summary>
    public Action? RequestShowWindow { get; set; }

    /// <summary>Set by the window: shuts the application down (tray "Exit").</summary>
    public Action? RequestCloseApp { get; set; }

    /// <summary>Set by the window: opens the log viewer window.</summary>
    public Action? RequestOpenLogViewer { get; set; }

    public ShellViewModel(
        OverviewViewModel overviewVm,
        ProgramsViewModel programsVm,
        WindowsUpdatesViewModel windowsUpdatesVm,
        DriversViewModel driversVm,
        PipPackagesViewModel pipPackagesVm,
        SettingsViewModel settingsVm,
        HardwareHubViewModel hardwareHubVm,
        LogConsoleViewModel logConsole,
        IAppUpdateService appUpdateService,
        ISettingsRepository settingsRepository,
        ILoggerService logger)
    {
        _overviewVm = overviewVm;
        _programsVm = programsVm;
        _windowsUpdatesVm = windowsUpdatesVm;
        _driversVm = driversVm;
        _pipPackagesVm = pipPackagesVm;
        _settingsVm = settingsVm;
        _hardwareHubVm = hardwareHubVm;
        LogConsole = logConsole;
        _appUpdateService = appUpdateService;
        _settingsRepository = settingsRepository;
        _logger = logger;

        NavigateTo(AppPage.Overview);

        IsOnline = _networkMonitor.IsOnline;
        _networkMonitor.OnlineStatusChanged += online =>
            Application.Current.Dispatcher.InvokeAsync(() => IsOnline = online);
    }

    [RelayCommand]
    public void NavigateTo(AppPage page)
    {
        SelectedPage = page;
        CurrentPage = page switch
        {
            AppPage.Overview       => _overviewVm,
            AppPage.Programs       => _programsVm,
            AppPage.WindowsUpdates => _windowsUpdatesVm,
            AppPage.Drivers        => _driversVm,
            AppPage.PipPackages    => _pipPackagesVm,
            AppPage.HardwareHub    => _hardwareHubVm,
            AppPage.Settings       => _settingsVm,
            _                      => _overviewVm
        };
    }

    /// <summary>
    /// Kicks off a scan on every source page together and reports a single busy state
    /// (<see cref="IsScanningAll"/>) so the sidebar button shows a spinner until every
    /// scan finishes. Concurrent invocations are allowed at the command level but guarded
    /// here, so re-clicking while a run is active is a harmless no-op.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ScanAllAsync()
    {
        if (IsScanningAll)
        {
            return;
        }

        IsScanningAll = true;
        try
        {
            await Task.WhenAll(
                ExecuteIfPossible(_programsVm.ScanCommand),
                ExecuteIfPossible(_windowsUpdatesVm.ScanCommand),
                ExecuteIfPossible(_driversVm.ScanCommand),
                ExecuteIfPossible(_pipPackagesVm.ScanCommand));
        }
        finally
        {
            IsScanningAll = false;
        }

        static Task ExecuteIfPossible(IAsyncRelayCommand command) =>
            command.CanExecute(null) ? command.ExecuteAsync(null) : Task.CompletedTask;
    }

    [RelayCommand]
    private void ShowWindow() => RequestShowWindow?.Invoke();

    [RelayCommand]
    private void ExitApplication() => RequestCloseApp?.Invoke();

    [RelayCommand]
    private void OpenLogViewer() => RequestOpenLogViewer?.Invoke();

    [RelayCommand]
    private void DownloadAppUpdate()
    {
        if (string.IsNullOrWhiteSpace(_appUpdateUrl))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_appUpdateUrl)
        {
            UseShellExecute = true
        });
    }

    /// <summary>
    /// One-shot work after the window is shown: app self-update check, then (only if the
    /// user enabled it) a Programs scan. Centralizing this here is what removes the old
    /// double-scan race between App startup and the view model constructor.
    /// </summary>
    public async Task RunStartupTasksAsync()
    {
        await CheckForAppUpdateAsync();

        try
        {
            var settings = await _settingsRepository.LoadAsync();
            if (settings.ScanOnStartup && _programsVm.ScanCommand.CanExecute(null))
            {
                _programsVm.ScanCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            _logger.Info($"Startup scan trigger failed (non-fatal): {ex.Message}");
        }

        // Detect hardware and load guides now (not lazily on first visit to the Guides page),
        // so the sidebar's category sub-branches are populated as soon as the app opens.
        _ = _hardwareHubVm.InitializeAsync();
    }

    /// <summary>
    /// Re-verifies dynamic guide state (e.g. an NVIDIA driver version) when the window
    /// regains focus — the natural moment the user has just come back from installing
    /// something outside the app.
    /// </summary>
    public Task RefreshGuidesAfterActivationAsync() => _hardwareHubVm.RefreshAfterPossibleExternalChangeAsync();

    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
            var (hasUpdate, url) = await _appUpdateService.CheckForAppUpdatesAsync(currentVersion);

            if (hasUpdate)
            {
                _appUpdateUrl = url;
                HasAppUpdate = true;
                WeakReferenceMessenger.Default.Send(
                    new NotificationMessage("A new version of XenUpdate is available!"));
            }
        }
        catch (Exception ex)
        {
            _logger.Info($"App update check failed (non-fatal): {ex.Message}");
        }
    }
}
