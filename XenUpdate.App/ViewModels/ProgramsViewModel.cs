using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using XenUpdate.App.Collections;
using XenUpdate.App.Messages;
using XenUpdate.App.Services;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

file static class L
{
    public static string T(string key) => LocalizationManager.Instance[key];
}

/// <summary>
/// ViewModel for the Programs page.
/// Drives the winget scan flow, selected update flow, and page busy state.
/// </summary>
public sealed partial class ProgramsViewModel : ObservableObject
{
    private readonly IWingetScanner _scanner;
    private readonly IWingetInstaller _installer;
    private readonly IBlacklistRepository _blacklistRepository;
    private readonly IWhitelistRepository _whitelistRepository;
    private readonly ILoggerService _logger;

    private CancellationTokenSource? _operationCts;

    /// <summary>
    /// Tracks every <see cref="AppUpdateItem"/> we have subscribed to so we can
    /// cleanly unsubscribe on a collection Reset (raised by <c>ReplaceAll</c>).
    /// On a Reset, <c>e.OldItems</c> is null, so without this list we would leak
    /// old subscriptions and miss new ones — causing the button to stay disabled.
    /// </summary>
    private readonly List<AppUpdateItem> _subscribedItems = new();

    /// <summary>
    /// Local copy of every item returned by the most recent successful winget scan.
    /// Never cleared by blacklist operations, so when the user un-blacklists an entry
    /// in Settings, <see cref="RestoreUnblacklistedItemsAsync"/> can re-add matching
    /// items to <see cref="Updates"/> without requiring a full rescan.
    /// Reset only when a new scan succeeds.
    /// </summary>
    private List<AppUpdateItem> _lastScanResults = new();

    private CancellationTokenSource? _blacklistRestoreDebounceCts;

    // Captured from the failure-reason progress event during a single item install.
    // Reset to null before each item; read once after InstallUpdateAsync returns false.
    // FailureReason is a stable key (e.g. "NoInternet"), not display text — Infrastructure
    // has no localization dependency, so this layer resolves it to the active language.
    private string? _lastItemFailureReasonKey;
    private string? _lastItemFailureDetail;

    /// <summary>
    /// Succeeded/failed counts from the most recently completed install batch on this page.
    /// Read by ShellViewModel right after InstallSelectedCommand finishes to build the
    /// cross-page "Update All" summary — captured here rather than re-derived from Updates
    /// afterward, since succeeded rows are quietly removed from Updates a couple of seconds
    /// after finishing (see RemoveSucceededItemsLocallyAsync), which would undercount them.
    /// </summary>
    public int LastBatchSucceededCount { get; private set; }
    public int LastBatchFailedCount { get; private set; }

    /// <summary>True while any operation (scan or install) is running. Drives command enable/disable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private bool _isBusy;

    /// <summary>True only while a scan (initial or post-install refresh) is running.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>
    /// A simulated scan progress percentage (winget doesn't report real scan progress).
    /// Climbs quickly at first then eases off, capped at 92 until the scan actually
    /// finishes, at which point it jumps to 100 — gives a "this is completing" feel
    /// instead of an indefinite spinner with no sense of how far along it is.
    /// </summary>
    [ObservableProperty]
    private int _scanProgressPercent;

    private readonly System.Windows.Threading.DispatcherTimer _scanProgressTimer =
        new() { Interval = TimeSpan.FromMilliseconds(150) };

    /// <summary>Short status line shown below the DataGrid.</summary>
    [ObservableProperty]
    private string _statusMessage = L.T("StatusProgramsInitial");

    /// <summary>True after the user has completed at least one scan attempt.</summary>
    [ObservableProperty]
    private bool _hasScanned;

    /// <summary>True when the current result set contains one or more updates.</summary>
    [ObservableProperty]
    private bool _hasUpdates;

    /// <summary>True while the selected-app Winget update batch is running.</summary>
    [ObservableProperty]
    private bool _isUpdateBatchRunning;

    /// <summary>Shows the current item position within the selected update batch.</summary>
    [ObservableProperty]
    private string _currentBatchProgressText = string.Empty;

    /// <summary>Shows the display name of the application currently being updated.</summary>
    [ObservableProperty]
    private string _currentAppName = string.Empty;

    /// <summary>Shows the completed-item batch percentage for the active update run.</summary>
    [ObservableProperty]
    private int _overallProgressPercent;

    /// <summary>Shows the current-item progress percentage when a reliable value is available.</summary>
    [ObservableProperty]
    private int _currentItemProgressPercent;

    /// <summary>True when the current-item progress bar should stay indeterminate.</summary>
    [ObservableProperty]
    private bool _isCurrentItemProgressIndeterminate = true;

    /// <summary>Shows the current-item progress text in a user-friendly way.</summary>
    [ObservableProperty]
    private string _currentItemProgressText = string.Empty;

    /// <summary>Shows extra progress detail for the current application update.</summary>
    [ObservableProperty]
    private string _currentInstallDetailText = string.Empty;

    /// <summary>True when the last scan attempt threw an exception (network outage, permission error, etc.).</summary>
    [ObservableProperty]
    private bool _hasScanFailed;

    /// <summary>True when the Programs empty-state panel should be shown instead of the grid.</summary>
    public bool IsEmptyStateVisible => HasScanned && !HasUpdates && !IsBusy;

    /// <summary>
    /// Tri-state "select all" backing the grid's header checkbox: true when every row
    /// is selected, false when none are, null (indeterminate) for a partial selection.
    /// Setting it selects or clears every visible row in one click.
    /// </summary>
    public bool? AreAllSelected
    {
        get
        {
            if (Updates.Count == 0)
            {
                return false;
            }

            var selectedCount = Updates.Count(item => item.IsSelected);
            return selectedCount == 0 ? false
                 : selectedCount == Updates.Count ? true
                 : null;
        }
        set
        {
            if (value is not bool target)
            {
                return;
            }

            foreach (var item in Updates)
            {
                item.IsSelected = target;
            }

            OnPropertyChanged(nameof(AreAllSelected));
        }
    }

    /// <summary>
    /// The list of available application updates displayed in the DataGrid.
    /// Always populated on the UI thread. Supports bulk replacement so scan results
    /// can be swapped in with a single Reset notification rather than N Add events.
    /// </summary>
    public BulkObservableCollection<AppUpdateItem> Updates { get; } = new();

    /// <summary>
    /// Initializes the ViewModel with its required services.
    /// All services are injected by the DI container.
    /// </summary>
    public ProgramsViewModel(
        IWingetScanner scanner,
        IWingetInstaller installer,
        IBlacklistRepository blacklistRepository,
        IWhitelistRepository whitelistRepository,
        ILoggerService logger)
    {
        _scanner = scanner;
        _installer = installer;
        _blacklistRepository = blacklistRepository;
        _whitelistRepository = whitelistRepository;
        _logger = logger;

        Updates.CollectionChanged += OnUpdatesCollectionChanged;

        // When any code removes an entry from the blacklist the repository fires
        // BlacklistChanged. We listen here so items can be restored instantly without
        // a manual rescan whenever the user un-blacklists something in Settings.
        _blacklistRepository.BlacklistChanged += OnBlacklistChangedExternally;

        // Same idea for the whitelist: the Settings page (or this page's own context menu)
        // can add/remove an entry from anywhere, so the star badge here must stay in sync
        // instead of only reflecting whatever was true at the last scan.
        _whitelistRepository.WhitelistChanged += OnWhitelistChangedExternally;

        _scanProgressTimer.Tick += OnScanProgressTick;
    }

    private void OnScanProgressTick(object? sender, EventArgs e)
    {
        var remaining = 92 - ScanProgressPercent;
        if (remaining <= 0)
        {
            return;
        }

        ScanProgressPercent = Math.Min(92, ScanProgressPercent + Math.Max(1, remaining / 8));
    }

    /// <summary>
    /// Scans for available winget updates in the background.
    /// The UI thread is never blocked because winget process I/O is awaited asynchronously.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ResetOperationCancellation();
        ClearUpdateFeedback();

        IsBusy = true;
        IsScanning = true;
        HasScanned = false;
        HasUpdates = false;
        HasScanFailed = false;
        StatusMessage = L.T("StatusScanning");
        Updates.Clear();

        ScanProgressPercent = 0;
        _scanProgressTimer.Start();

        try
        {
            var results = await _scanner.GetAvailableUpdatesAsync(_operationCts!.Token);

            // Continuation of `await` resumes on the UI thread (WPF sync context),
            // so we can mutate the observable collection directly here.
            foreach (var item in results)
            {
                item.Status = UpdateStatus.Pending;
            }

            // Snapshot the raw scan results. This is the source of truth for instant
            // blacklist-remove restores; it is never modified by blacklist operations.
            _lastScanResults = results.ToList();

            Updates.ReplaceAll(results);
            await ApplyWhitelistStateAsync();

            HasUpdates = Updates.Count > 0;
            HasScanned = true;
            ScanProgressPercent = 100;

            StatusMessage = Updates.Count == 0
                ? L.T("ProgramsEmptyTitle")
                : string.Format(L.T("StatusUpdatesAvailable"), Updates.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L.T("StatusScanCancelled");
            _logger.Info("Programs scan was cancelled by the user.");
        }
        catch (Exception ex)
        {
            HasScanFailed = true;
            StatusMessage = L.T("StatusScanFailed");
            _logger.Error("Programs scan encountered an unexpected error.", ex);
        }
        finally
        {
            _scanProgressTimer.Stop();
            CompleteOperation();
        }
    }

    private bool CanScan() => !IsBusy;

    /// <summary>Cancels the currently running scan or update batch.</summary>
    [RelayCommand]
    private void CancelScan()
    {
        if (!IsBusy || _operationCts is null)
        {
            return;
        }

        StatusMessage = L.T("Cancelling");

        if (IsUpdateBatchRunning)
        {
            CurrentInstallDetailText = L.T("CancellingWingetOp");
        }

        _operationCts.Cancel();
    }

    /// <summary>
    /// Updates all applications where <see cref="AppUpdateItem.IsSelected"/> is true.
    /// Selected items are processed one by one. On clean completion an automatic
    /// refresh scan is triggered after a short delay so stale rows are pruned.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallSelectedAsync()
    {
        if (IsBusy)
        {
            _logger.Warning("Update request ignored because another operation is already running.");
            return;
        }

        var selectedItems = Updates.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            StatusMessage = L.T("SelectAtLeastOneApp");
            _logger.Info("Programs update requested with no selected applications.");
            return;
        }

        WeakReferenceMessenger.Default.Send(new InstallBatchStartedMessage(L.T("NavPrograms"), selectedItems));
        await RunInstallBatchAsync(selectedItems);
    }

    /// <summary>
    /// Runs the install batch over exactly the given items, in the given order — no selection
    /// snapshot, no InstallBatchStartedMessage (the caller already announced this batch to the
    /// Update Queue window, e.g. via ShellViewModel.UpdateAllAsync's AnnouncePlan). This is what
    /// makes item-level drag-reordering inside the Update Queue window actually change
    /// execution order rather than just the visual: ShellViewModel reads UpdateQueueGroup.Items'
    /// live order and passes it straight here instead of letting this page recompute its own
    /// order from Updates.
    /// </summary>
    public async Task InstallItemsAsync(IReadOnlyList<UpdateItem> items) =>
        await RunInstallBatchAsync(items.Cast<AppUpdateItem>().ToList());

    private async Task RunInstallBatchAsync(List<AppUpdateItem> selectedItems)
    {
        ResetOperationCancellation();

        IsBusy = true;
        IsUpdateBatchRunning = true;
        ResetProgressFeedback();
        _logger.Info("Winget update batch started.");
        _logger.Info($"{selectedItems.Count} application(s) selected for update.");

        var succeededItems = new List<AppUpdateItem>();
        var failedCount = 0;
        var batchCompletedCleanly = false;

        try
        {
            for (var index = 0; index < selectedItems.Count; index++)
            {
                _operationCts!.Token.ThrowIfCancellationRequested();

                var item = selectedItems[index];
                await ShowInstallingStateAsync(item, index, selectedItems.Count);

                try
                {
                    _lastItemFailureReasonKey = null;
                    _lastItemFailureDetail = null;
                    SetCurrentInstallPhase(L.T("PhaseInstalling"), null);
                    var progress = new Progress<InstallProgress>(update => OnInstallProgressReported(item, update));
                    var success = await _installer.InstallUpdateAsync(item, progress, _operationCts.Token);

                    // Set the reason before flipping Status so the failure-details row
                    // renders with its text already in place. Fall back to a generic
                    // message: the details strip must never appear empty.
                    item.ErrorMessage = success
                        ? null
                        : ResolveFailureMessage(_lastItemFailureReasonKey, _lastItemFailureDetail);
                    item.Status = success ? UpdateStatus.Succeeded : UpdateStatus.Failed;

                    if (success)
                    {
                        succeededItems.Add(item);
                    }
                    else
                    {
                        failedCount++;
                    }

                    UpdateOverallProgress(succeededItems.Count + failedCount, selectedItems.Count);
                }
                catch (OperationCanceledException)
                {
                    item.Status = UpdateStatus.Pending;
                    throw;
                }
            }

            StatusMessage = string.Format(L.T("AppUpdatesCompleted"), succeededItems.Count, failedCount);
            _logger.Info($"Winget update batch completed. Total: {selectedItems.Count}, Succeeded: {succeededItems.Count}, Failed: {failedCount}.");
            batchCompletedCleanly = true;
            LastBatchSucceededCount = succeededItems.Count;
            LastBatchFailedCount = failedCount;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = string.Format(L.T("AppUpdatesCancelled"), succeededItems.Count, failedCount);
            LastBatchSucceededCount = succeededItems.Count;
            LastBatchFailedCount = failedCount;
            _logger.Info($"Winget update batch was cancelled. Completed before cancel: {succeededItems.Count + failedCount} of {selectedItems.Count}.");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("UpdateBatchFailed");
            _logger.Error("Winget update batch encountered an unexpected error.", ex);
        }
        finally
        {
            CompleteOperation();
        }

        if (batchCompletedCleanly && succeededItems.Count > 0)
        {
            await RemoveSucceededItemsLocallyAsync(succeededItems);
        }
    }

    /// <summary>
    /// Waits briefly so the user can see the final <c>Succeeded</c> row states,
    /// then quietly removes successfully updated rows from the visible list.
    /// No full rescan is performed; the user can press 'Scan for Updates' to
    /// re-verify when they want.
    /// </summary>
    private async Task RemoveSucceededItemsLocallyAsync(List<AppUpdateItem> succeededItems)
    {
        // Short pause so the Succeeded state is visible before the row disappears.
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Another operation may have started during the pause (e.g. user pressed Scan).
        // In that case, skip the silent removal to avoid fighting the new list.
        if (IsBusy)
        {
            _logger.Info("Programs local removal skipped because another operation started during the pause.");
            return;
        }

        foreach (var item in succeededItems)
        {
            Updates.Remove(item);
        }

        HasUpdates = Updates.Count > 0;
        NotifyVisibilityPropertiesChanged();
        InstallSelectedCommand.NotifyCanExecuteChanged();

        StatusMessage = Updates.Count == 0
            ? L.T("AppUpdateCompleteNone")
            : string.Format(L.T("AppUpdateCompleteRemaining"), Updates.Count);

        _logger.Info($"Programs removed {succeededItems.Count} succeeded row(s) from the visible list.");
    }

    private bool CanInstall() => !IsBusy && Updates.Any(item => item.IsSelected);

    /// <summary>
    /// Adds the given program rows to the blacklist and removes them from the visible
    /// list immediately. Rows that are already blacklisted are also removed from the
    /// visible list, because the user's intent is to hide them right now.
    /// </summary>
    /// <param name="items">The program rows to blacklist.</param>
    /// <returns>The number of package IDs newly added to the blacklist.</returns>
    public async Task<int> AddItemsToBlacklistAsync(IEnumerable<AppUpdateItem> items)
    {
        var candidates = items
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.WingetPackageId))
            .GroupBy(item => item.WingetPackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (candidates.Count == 0)
        {
            StatusMessage = L.T("NoPackageIdsToBlacklist");
            return 0;
        }

        // Immediate local removal: hide every selected row from the grid before any I/O.
        // This keeps the UI responsive and avoids waiting for a repository refresh.
        var candidateIds = candidates.Select(c => c.WingetPackageId).ToList();
        RemoveVisibleItemsByPackageId(candidateIds);

        // Persist additions. Rows already in the repository are left untouched.
        var existingIds = await _blacklistRepository.GetBlacklistedIdsAsync();
        var knownIds = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedIds = new List<string>();

        foreach (var item in candidates)
        {
            if (!knownIds.Add(item.WingetPackageId))
            {
                continue;
            }

            await _blacklistRepository.AddAsync(item.WingetPackageId);
            addedIds.Add(item.WingetPackageId);
        }

        if (addedIds.Count == 0)
        {
            StatusMessage = candidateIds.Count == 1
                ? L.T("AlreadyBlacklistedHidden")
                : string.Format(L.T("HiddenAlreadyBlacklisted"), candidateIds.Count);
            return 0;
        }

        StatusMessage = addedIds.Count == 1
            ? string.Format(L.T("AddedSingleToBlacklist"), addedIds[0])
            : string.Format(L.T("AddedMultipleToBlacklist"), addedIds.Count);

        _logger.Info($"Programs page added {addedIds.Count} package ID(s) to blacklist.");
        return addedIds.Count;
    }

    /// <summary>Refreshes <see cref="UpdateItem.IsWhitelisted"/> on every visible row from the repository.</summary>
    private async Task ApplyWhitelistStateAsync()
    {
        var whitelistedIds = await _whitelistRepository.GetWhitelistedIdsAsync(UpdateSource.Winget);
        var set = whitelistedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Updates)
        {
            item.IsWhitelisted = set.Contains(item.Id);
        }
    }

    /// <summary>
    /// Called whenever the whitelist changes from any source (this page's own context menu,
    /// another page, or the Settings page). Re-applies whitelist state to the currently
    /// visible rows so a star badge never lingers after the entry was removed elsewhere.
    /// </summary>
    private void OnWhitelistChangedExternally()
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await ApplyWhitelistStateAsync();
        });
    }

    /// <summary>
    /// Adds the given rows to the whitelist — pre-approving them to install themselves the
    /// moment the app is next online. Unlike blacklisting, whitelisted rows stay visible.
    /// </summary>
    public async Task<int> AddItemsToWhitelistAsync(IEnumerable<AppUpdateItem> items)
    {
        var candidates = items.Where(item => item is not null && !item.IsWhitelisted && !string.IsNullOrWhiteSpace(item.Id)).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        foreach (var item in candidates)
        {
            await _whitelistRepository.AddAsync(UpdateSource.Winget, item.Id, item.DisplayName);
            item.IsWhitelisted = true;
        }

        StatusMessage = candidates.Count == 1
            ? string.Format(L.T("AddedSingleToWhitelist"), candidates[0].DisplayName)
            : string.Format(L.T("AddedMultipleToWhitelist"), candidates.Count);

        _logger.Info($"Programs page added {candidates.Count} item(s) to the whitelist.");
        return candidates.Count;
    }

    /// <summary>Removes the given rows from the whitelist.</summary>
    public async Task<int> RemoveItemsFromWhitelistAsync(IEnumerable<AppUpdateItem> items)
    {
        var candidates = items.Where(item => item is not null && item.IsWhitelisted).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        foreach (var item in candidates)
        {
            await _whitelistRepository.RemoveAsync(UpdateSource.Winget, item.Id);
            item.IsWhitelisted = false;
        }

        StatusMessage = candidates.Count == 1
            ? string.Format(L.T("RemovedSingleFromWhitelist"), candidates[0].DisplayName)
            : string.Format(L.T("RemovedMultipleFromWhitelist"), candidates.Count);

        _logger.Info($"Programs page removed {candidates.Count} item(s) from the whitelist.");
        return candidates.Count;
    }

    /// <summary>
    /// Scans if needed, then silently installs any known update whose ID is on the
    /// whitelist. Triggered when the app detects it just came online, so pre-approved
    /// apps update themselves without the user ever opening this page.
    /// </summary>
    public async Task RunWhitelistedAutoUpdateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var whitelistedIds = await _whitelistRepository.GetWhitelistedIdsAsync(UpdateSource.Winget);
        if (whitelistedIds.Count == 0)
        {
            return;
        }

        if (!HasScanned && ScanCommand.CanExecute(null))
        {
            await ScanCommand.ExecuteAsync(null);
        }

        if (IsBusy)
        {
            return;
        }

        var targets = Updates.Where(item => item.IsWhitelisted && item.Status == UpdateStatus.Pending).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var item in targets)
        {
            item.IsSelected = true;
        }

        if (InstallSelectedCommand.CanExecute(null))
        {
            _logger.Info($"Auto-installing {targets.Count} whitelisted app update(s) after coming online.");
            await InstallSelectedCommand.ExecuteAsync(null);
        }
    }

    private async Task ShowInstallingStateAsync(AppUpdateItem item, int currentIndex, int totalCount)
    {
        item.Status = UpdateStatus.Installing;
        item.ProgressPercent = 0;
        CurrentBatchProgressText = string.Format(L.T("UpdatingXOfY"), currentIndex + 1, totalCount);
        CurrentAppName = item.DisplayName;
        CurrentItemProgressPercent = 0;
        IsCurrentItemProgressIndeterminate = true;
        CurrentItemProgressText = L.T("CurrentItemInProgress");
        SetCurrentInstallPhase(L.T("PhasePreparing"), L.T("UpdatingSelectedApps"));

        await Task.Yield();
    }

    private void OnInstallProgressReported(AppUpdateItem item, InstallProgress update)
    {
        // Failure reason arrives on the final progress event when the install fails.
        // Store it so InstallSelectedAsync can attach it to the item and status bar.
        if (update.FailureReason is not null)
        {
            _lastItemFailureReasonKey = update.FailureReason;
            _lastItemFailureDetail = update.FailureDetail;
            return;
        }

        // Live download size, straight from winget (e.g. "12.4 MB / 84.0 MB"). Units are
        // locale-neutral, so this reads correctly in any language.
        var hasDownloadText = !string.IsNullOrEmpty(update.DownloadText);
        if (hasDownloadText)
        {
            CurrentItemProgressText = update.DownloadText!;
        }

        if (update.Percent is > 0 and < 100)
        {
            item.ProgressPercent = update.Percent;
            CurrentItemProgressPercent = update.Percent;
            IsCurrentItemProgressIndeterminate = false;
            SetCurrentInstallPhase(hasDownloadText ? L.T("PhaseDownloading") : L.T("PhaseInstalling"), null);
        }
        else if (update.Percent >= 100)
        {
            item.ProgressPercent = 100;
            CurrentItemProgressPercent = 0;
            IsCurrentItemProgressIndeterminate = true;
            if (!hasDownloadText)
            {
                CurrentItemProgressText = L.T("PhaseFinalizing");
            }
            SetCurrentInstallPhase(L.T("PhaseFinalizing"), null);
        }
    }

    private void OnUpdatesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // ReplaceAll (and Clear) raise Reset where e.OldItems/e.NewItems are null.
            // Unsubscribe from everything we tracked, then subscribe to what is now in the list.
            UnsubscribeAllItems();
            SubscribeAllItems();
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<AppUpdateItem>())
                {
                    item.PropertyChanged -= OnUpdateItemPropertyChanged;
                    _subscribedItems.Remove(item);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<AppUpdateItem>())
                {
                    item.PropertyChanged += OnUpdateItemPropertyChanged;
                    _subscribedItems.Add(item);
                }
            }
        }

        InstallSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AreAllSelected));
    }

    /// <summary>Removes <see cref="OnUpdateItemPropertyChanged"/> from every tracked item and clears the tracking list.</summary>
    private void UnsubscribeAllItems()
    {
        foreach (var item in _subscribedItems)
        {
            item.PropertyChanged -= OnUpdateItemPropertyChanged;
        }
        _subscribedItems.Clear();
    }

    /// <summary>Subscribes <see cref="OnUpdateItemPropertyChanged"/> to every item currently in <see cref="Updates"/> and records them.</summary>
    private void SubscribeAllItems()
    {
        foreach (var item in Updates)
        {
            item.PropertyChanged += OnUpdateItemPropertyChanged;
            _subscribedItems.Add(item);
        }
    }

    private void OnUpdateItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppUpdateItem.IsSelected))
        {
            InstallSelectedCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AreAllSelected));
        }
    }

    /// <summary>
    /// Called when the blacklist repository signals a change from any source.
    /// Dispatches to the UI thread so the collection update is safe.
    /// </summary>
    private void OnBlacklistChangedExternally()
    {
        _blacklistRestoreDebounceCts?.Cancel();
        _blacklistRestoreDebounceCts = new CancellationTokenSource();
        var debounceToken = _blacklistRestoreDebounceCts.Token;

        // The event may fire from any thread; InvokeAsync queues work on the dispatcher.
        // A tiny debounce lets multi-add/multi-remove repository writes settle before
        // we diff the cache, avoiding restore flicker during Programs-page blacklist adds.
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(100, debounceToken);
                await RestoreUnblacklistedItemsAsync();
            }
            catch (OperationCanceledException)
            {
                // A newer blacklist change arrived; that later handler will do the restore.
            }
        });
    }

    /// <summary>
    /// Compares the current blacklist against <see cref="_lastScanResults"/> and adds
    /// any items that are no longer blacklisted back to the visible <see cref="Updates"/>
    /// list. Items that were already successfully installed (Status == Succeeded) are
    /// skipped; items already visible are skipped to prevent duplicates.
    ///
    /// This is a local-only operation — no winget process is launched.
    /// </summary>
    private async Task RestoreUnblacklistedItemsAsync()
    {
        // Nothing to restore if we have no cached scan or if an operation is running.
        if (_lastScanResults.Count == 0 || IsBusy)
        {
            return;
        }

        var blacklistedIds = await _blacklistRepository.GetBlacklistedIdsAsync();
        var blacklistSet = blacklistedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Package IDs already in the visible list — don't add duplicates.
        var visibleIds = Updates
            .Select(u => u.WingetPackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Items from the last scan that are now unblacklisted and not yet visible.
        // Skip items that were already installed (Status == Succeeded was set during install).
        var toRestore = _lastScanResults
            .Where(item =>
                !blacklistSet.Contains(item.WingetPackageId) &&
                !visibleIds.Contains(item.WingetPackageId) &&
                item.Status != UpdateStatus.Succeeded)
            .ToList();

        if (toRestore.Count == 0)
        {
            return;
        }

        // Reset each restored item's status back to Pending so it shows the badge correctly.
        foreach (var item in toRestore)
        {
            item.Status = UpdateStatus.Pending;
        }

        // Build the new visible list: restored items first so the user notices them,
        // then the surviving current items. Filter current visible items against the
        // blacklist too, in case something was blacklisted concurrently (edge case).
        var currentVisibleItems = Updates
            .Where(u => !blacklistSet.Contains(u.WingetPackageId))
            .ToList();

        var newList = toRestore
            .Concat(currentVisibleItems)
            .ToList();

        Updates.ReplaceAll(newList);

        HasUpdates = Updates.Count > 0;
        HasScanned = true;
        NotifyVisibilityPropertiesChanged();
        InstallSelectedCommand.NotifyCanExecuteChanged();

        StatusMessage = string.Format(L.T("RestoredFromBlacklist"), toRestore.Count);

        _logger.Info($"Programs restored {toRestore.Count} item(s) from scan cache after blacklist change.");
    }

    /// <summary>
    /// Removes every item whose <see cref="AppUpdateItem.WingetPackageId"/> is in
    /// <paramref name="packageIds"/> from the visible list without performing a full scan.
    ///
    /// We use <see cref="BulkObservableCollection{T}.ReplaceAll"/> (a single Reset event)
    /// rather than N individual <c>Remove</c> calls.  After the scan populates the DataGrid
    /// via <c>ReplaceAll</c>, the DataGrid's internal row-to-index map is rebuilt once.
    /// Subsequent individual <c>Remove</c> events carry an item index; if that index is
    /// stale (a known WPF DataGrid + <c>VirtualizationMode=Recycling</c> edge case), the
    /// visual row is not removed even though the backing collection was modified.  A Reset
    /// event always forces the DataGrid to re-query the collection from scratch, so the
    /// visual state is guaranteed to be consistent.
    /// </summary>
    private void RemoveVisibleItemsByPackageId(IEnumerable<string> packageIds)
    {
        var idSet = packageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Keep only items that are NOT in the blacklisted set.
        var remaining = Updates
            .Where(item => !idSet.Contains(item.WingetPackageId))
            .ToList();

        // Single Reset event — DataGrid rebuilds visual rows from scratch, which is
        // always reliable regardless of prior virtualization state.
        Updates.ReplaceAll(remaining);

        HasUpdates = Updates.Count > 0;
        NotifyVisibilityPropertiesChanged();
    }

    partial void OnHasScannedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    partial void OnHasUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    private void ResetOperationCancellation()
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
    }

    private void NotifyVisibilityPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    private void ResetProgressFeedback()
    {
        OverallProgressPercent = 0;
        CurrentItemProgressPercent = 0;
        IsCurrentItemProgressIndeterminate = true;
        CurrentItemProgressText = L.T("CurrentItemInProgress");
    }

    private void UpdateOverallProgress(int completedItemCount, int totalItemCount)
    {
        if (totalItemCount <= 0)
        {
            OverallProgressPercent = 0;
            return;
        }

        OverallProgressPercent = completedItemCount * 100 / totalItemCount;
    }

    /// <summary>
    /// Resolves a winget failure key (e.g. <c>"NoInternet"</c>, <c>"NeedsAdmin"</c>) reported by
    /// <see cref="IWingetInstaller"/> to display text in the currently active language. Winget
    /// itself never returns a key, so a null/unrecognized one falls back to the generic message.
    /// </summary>
    private static string ResolveFailureMessage(string? reasonKey, string? detail)
    {
        if (string.IsNullOrEmpty(reasonKey))
        {
            return L.T("InstallFailedGeneric");
        }

        var template = L.T($"WingetFail_{reasonKey}");
        return detail is not null ? string.Format(template, detail) : template;
    }

    private void SetCurrentInstallPhase(string phaseText, string? statusMessage)
    {
        CurrentInstallDetailText = phaseText;

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            StatusMessage = statusMessage;
        }
    }

    private void CompleteOperation()
    {
        IsBusy = false;
        IsScanning = false;
        InstallSelectedCommand.NotifyCanExecuteChanged();
        ScanCommand.NotifyCanExecuteChanged();
        ClearUpdateFeedback();
        NotifyVisibilityPropertiesChanged();
    }

    private void ClearUpdateFeedback()
    {
        IsUpdateBatchRunning = false;
        CurrentBatchProgressText = string.Empty;
        CurrentAppName = string.Empty;
        OverallProgressPercent = 0;
        CurrentItemProgressPercent = 0;
        IsCurrentItemProgressIndeterminate = true;
        CurrentItemProgressText = string.Empty;
        CurrentInstallDetailText = string.Empty;
    }
}
