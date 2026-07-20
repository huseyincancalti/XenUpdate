using System.Net.NetworkInformation;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using XenUpdate.App.Messages;
using XenUpdate.App.Services;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

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
public sealed partial class ShellViewModel : ObservableObject, IDisposable
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

    private DateTime _lastWhitelistAutoUpdateRun = DateTime.MinValue;

    // A flapping connection (wifi dropping in and out) would otherwise fire a fresh scan on
    // every single reconnect; this keeps repeated triggers within a short window a no-op.
    private static readonly TimeSpan WhitelistAutoUpdateThrottle = TimeSpan.FromSeconds(30);

    public LogConsoleViewModel LogConsole { get; }

    /// <summary>Backs the update queue window. Exposed so MainWindow can wire its RequestShow/RequestOpenLog actions.</summary>
    public UpdateQueueViewModel UpdateQueue { get; }

    /// <summary>The Settings page VM. Also the single source of theme state for the title-bar toggle.</summary>
    public SettingsViewModel Settings => _settingsVm;

    /// <summary>The Guides page VM. Exposed so the sidebar can show its categories as sub-branches.</summary>
    public HardwareHubViewModel GuidesViewModel => _hardwareHubVm;

    /// <summary>The page ViewModel shown in the shell's content area.</summary>
    [ObservableProperty]
    private ObservableObject? _currentPage;

    /// <summary>
    /// The active navigation selection. This is the single source of truth for which page is
    /// showing: setting it (from anywhere — the sidebar click, a stat-card click on Overview, a
    /// sidebar guide sub-branch) both switches <see cref="CurrentPage"/> and keeps the sidebar's
    /// own highlighted item in sync, since <c>NavList</c> binds its <c>SelectedValue</c> straight
    /// to this property instead of driving it one-way from a code-behind event handler.
    /// </summary>
    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Overview;

    /// <summary>True when a newer XenUpdate release is available; drives the title-bar banner.</summary>
    [ObservableProperty]
    private bool _hasAppUpdate;

    /// <summary>True while a "Scan All" run is in progress; drives the sidebar button's spinner/label.</summary>
    [ObservableProperty]
    private bool _isScanningAll;

    /// <summary>True while an "Update All" run is in progress; drives the sidebar button's spinner/label.</summary>
    [ObservableProperty]
    private bool _isUpdatingAll;

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
        UpdateQueueViewModel updateQueue,
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
        UpdateQueue = updateQueue;
        _appUpdateService = appUpdateService;
        _settingsRepository = settingsRepository;
        _logger = logger;

        // Set directly rather than through NavigateTo: SelectedPage's field already defaults to
        // AppPage.Overview, so calling NavigateTo(AppPage.Overview) here would be a no-op change
        // (the generated property setter skips notification when the value is unchanged) and
        // OnSelectedPageChanged would never fire to populate CurrentPage.
        CurrentPage = _overviewVm;

        IsOnline = _networkMonitor.IsOnline;
        _networkMonitor.OnlineStatusChanged += online =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => IsOnline = online);

            // Wifi/internet just came back — pre-approved (whitelisted) updates install
            // themselves right now, with no manual click needed on any page.
            if (online)
            {
                _ = RunWhitelistedAutoUpdatesAsync();
            }
        };
    }

    [RelayCommand]
    public void NavigateTo(AppPage page) => SelectedPage = page;

    /// <summary>
    /// Switches the displayed page content whenever SelectedPage changes, from any source —
    /// the sidebar's own SelectedValue binding, NavigateTo, or code setting SelectedPage
    /// directly (e.g. a guide sub-branch click). Centralizing this here is what keeps the
    /// sidebar highlight and the content area from ever disagreeing about the current page.
    /// </summary>
    partial void OnSelectedPageChanged(AppPage value)
    {
        CurrentPage = value switch
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

    /// <summary>
    /// Checks every row on all four scan pages at once — each page's own AreAllSelected
    /// setter already does the per-page bulk-select, this just fires all four together so
    /// "Update All" can be made to genuinely cover everything found without visiting each
    /// tab individually first. A no-op for any page with nothing scanned yet (Updates empty).
    /// </summary>
    [RelayCommand]
    private void SelectAllPending()
    {
        _programsVm.AreAllSelected = true;
        _windowsUpdatesVm.AreAllSelected = true;
        _driversVm.AreAllSelected = true;
        _pipPackagesVm.AreAllSelected = true;
    }

    /// <summary>
    /// One page's contribution to an "Update All" run — enough to announce it to the queue
    /// upfront and, later, actually run any of its items. Run takes the items to install
    /// explicitly (rather than the page just re-reading its own Updates.Where(IsSelected))
    /// because the queue window's flat list is freely drag-reorderable, even interleaving items
    /// from different sources — so the execution loop feeds each page exactly the item whose
    /// turn the live queue order says it is, one at a time.
    /// </summary>
    private sealed record UpdateAllCandidate(
        string Label,
        IReadOnlyList<UpdateItem> Items,
        Func<IReadOnlyList<UpdateItem>, Task> Run);

    /// <summary>
    /// Installs whatever is currently checked across all four scan pages. This does NOT select
    /// anything itself — it only acts on rows the user already checked via each page's own
    /// checkboxes (or SelectAllPending). An earlier version force-selected every still-Pending
    /// row regardless of what was actually checked, so clicking this after hand-picking a few
    /// items across different tabs silently installed everything else too — a real bug, not the
    /// intended "update my selections" behavior.
    /// Pages still run sequentially, not in parallel like <see cref="ScanAllAsync"/> — an install
    /// batch is far more consequential than a scan, and stacking four simultaneous install
    /// batches (winget + Windows Update + drivers + pip) would make progress reporting unreadable
    /// and risks resource contention between installers. But which page runs next is no longer
    /// fixed at Programs→WindowsUpdates→Drivers→PipPackages: every selected page's group is
    /// announced to the queue window upfront (so the whole plan is visible immediately, not
    /// discovered one "surprise" page at a time), and the execution order below re-reads the
    /// queue's live group order before each step — so dragging a not-yet-started group to the
    /// front in the Update Queue window actually changes what runs next, Steam-queue-style.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UpdateAllAsync()
    {
        if (IsUpdatingAll || IsScanningAll)
        {
            return;
        }

        IsUpdatingAll = true;
        try
        {
            var candidates = new List<UpdateAllCandidate>();

            if (_programsVm.Updates.Any(item => item.IsSelected) && _programsVm.InstallSelectedCommand.CanExecute(null))
            {
                candidates.Add(new UpdateAllCandidate(
                    LocalizationManager.Instance["NavPrograms"],
                    _programsVm.Updates.Where(item => item.IsSelected).ToList(),
                    orderedItems => _programsVm.InstallItemsAsync(orderedItems)));
            }

            if (_windowsUpdatesVm.Updates.Any(item => item.IsSelected) && _windowsUpdatesVm.InstallSelectedCommand.CanExecute(null))
            {
                candidates.Add(new UpdateAllCandidate(
                    LocalizationManager.Instance["NavWindowsUpdates"],
                    _windowsUpdatesVm.Updates.Where(item => item.IsSelected).ToList(),
                    orderedItems => _windowsUpdatesVm.InstallItemsAsync(orderedItems)));
            }

            if (_driversVm.Updates.Any(item => item.IsSelected) && _driversVm.InstallSelectedCommand.CanExecute(null))
            {
                candidates.Add(new UpdateAllCandidate(
                    LocalizationManager.Instance["NavDrivers"],
                    _driversVm.Updates.Where(item => item.IsSelected).ToList(),
                    orderedItems => _driversVm.InstallItemsAsync(orderedItems)));
            }

            if (_pipPackagesVm.Updates.Any(item => item.IsSelected) && _pipPackagesVm.InstallSelectedCommand.CanExecute(null))
            {
                candidates.Add(new UpdateAllCandidate(
                    LocalizationManager.Instance["NavPipPackages"],
                    _pipPackagesVm.Updates.Where(item => item.IsSelected).ToList(),
                    orderedItems => _pipPackagesVm.InstallItemsAsync(orderedItems)));
            }

            if (candidates.Count == 0)
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage(LocalizationManager.Instance["NoUpdatesSelected"]));
            }
            else
            {
                UpdateQueue.AnnouncePlan(candidates.Select(c => (c.Label, c.Items)).ToList());

                var byLabel = candidates.ToDictionary(c => c.Label);
                var plannedItems = candidates.SelectMany(c => c.Items).ToHashSet();

                // One item at a time, re-reading the queue's live flat order before every pick —
                // this, not the announce, is what makes drag-reordering in the queue window real:
                // whatever Pending row is highest when the previous install finishes is simply
                // what runs next, even if the user interleaved sources (a pip package between two
                // winget apps). The attempted set guards against an infinite loop when an item
                // comes back as Pending instead of Succeeded/Failed (each page's cancel path
                // deliberately resets in-flight items to Pending).
                var attempted = new HashSet<UpdateItem>();
                while (true)
                {
                    var nextEntry = UpdateQueue.Entries.FirstOrDefault(e =>
                        e.Item.Status == UpdateStatus.Pending
                        && plannedItems.Contains(e.Item)
                        && !attempted.Contains(e.Item)
                        && byLabel.ContainsKey(e.SourceLabel));
                    if (nextEntry is null)
                    {
                        break;
                    }

                    attempted.Add(nextEntry.Item);
                    await byLabel[nextEntry.SourceLabel].Run(new[] { nextEntry.Item });

                    // Every page's cancel path resets its in-flight item back to Pending instead
                    // of Succeeded/Failed — so "still Pending after its own run" reliably means
                    // the user hit Cancel. Stop the WHOLE run, not just this item: when batches
                    // became one-item-at-a-time, cancel silently degraded to skipping a single
                    // item while the loop marched on through everything else — the opposite of
                    // what pressing Cancel means.
                    if (nextEntry.Item.Status == UpdateStatus.Pending)
                    {
                        break;
                    }
                }

                var totalSucceeded = plannedItems.Count(i => i.Status == UpdateStatus.Succeeded);
                var totalFailed = plannedItems.Count(i => i.Status == UpdateStatus.Failed);

                // The in-app side of this is the update queue window (see UpdateQueueViewModel),
                // which was already popped open as AnnouncePlan ran and already shows the final
                // succeeded/failed state live — no separate summary notification needed here.
                // The Windows toast is still sent unconditionally; MainWindow's BalloonTipMessage
                // handler is the one place that decides whether the window is currently visible,
                // and only actually raises it when it isn't.
                var balloonText = string.Format(LocalizationManager.Instance["UpdateAllSummary"], totalSucceeded, totalFailed);
                WeakReferenceMessenger.Default.Send(new BalloonTipMessage(LocalizationManager.Instance["UpdateAllBalloonTitle"], balloonText));
            }
        }
        finally
        {
            IsUpdatingAll = false;
        }
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

        // The machine may already be online when the app starts (the common case) — run
        // whitelisted auto-updates now rather than waiting for a connectivity transition
        // that may never come during this session.
        if (_networkMonitor.IsOnline)
        {
            _ = RunWhitelistedAutoUpdatesAsync();
        }
    }

    /// <summary>
    /// Re-verifies dynamic guide state (e.g. an NVIDIA driver version) when the window
    /// regains focus — the natural moment the user has just come back from installing
    /// something outside the app.
    /// </summary>
    public Task RefreshGuidesAfterActivationAsync() => _hardwareHubVm.RefreshAfterPossibleExternalChangeAsync();

    /// <summary>
    /// Runs every page's whitelisted auto-update check in parallel. Each page ViewModel
    /// bails out immediately if it's busy or has nothing whitelisted, so this is cheap to
    /// call speculatively (app startup, every online transition).
    /// </summary>
    private async Task RunWhitelistedAutoUpdatesAsync()
    {
        if (DateTime.UtcNow - _lastWhitelistAutoUpdateRun < WhitelistAutoUpdateThrottle)
        {
            return;
        }

        _lastWhitelistAutoUpdateRun = DateTime.UtcNow;

        try
        {
            await Task.WhenAll(
                _programsVm.RunWhitelistedAutoUpdateAsync(),
                _windowsUpdatesVm.RunWhitelistedAutoUpdateAsync(),
                _driversVm.RunWhitelistedAutoUpdateAsync(),
                _pipPackagesVm.RunWhitelistedAutoUpdateAsync());
        }
        catch (Exception ex)
        {
            _logger.Error("Whitelisted auto-update run failed.", ex);
        }
    }

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

    /// <summary>
    /// Unsubscribes from <see cref="NetworkChange.NetworkAvailabilityChanged"/> via
    /// <see cref="_networkMonitor"/>. That event is backed by a dedicated, non-background
    /// OS-notification thread in the .NET networking stack — as long as anything is still
    /// subscribed, that thread keeps running, and a live foreground thread is enough to keep
    /// the whole process alive in Task Manager even after every window has closed and
    /// <c>Application.OnExit</c> has finished. ShellViewModel is registered as a DI singleton,
    /// so the container calls this automatically when <c>App.OnExit</c> disposes it.
    /// </summary>
    public void Dispose()
    {
        _networkMonitor.Dispose();
    }
}
