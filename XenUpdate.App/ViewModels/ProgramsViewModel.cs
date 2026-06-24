using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XenUpdate.App.Collections;
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

    /// <summary>True while any operation (scan or install) is running. Drives command enable/disable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private bool _isBusy;

    /// <summary>True only while a scan (initial or post-install refresh) is running.</summary>
    [ObservableProperty]
    private bool _isScanning;

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

    /// <summary>True when the Programs empty-state panel should be shown instead of the grid.</summary>
    public bool IsEmptyStateVisible => HasScanned && !HasUpdates && !IsBusy;

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
        ILoggerService logger)
    {
        _scanner = scanner;
        _installer = installer;
        _blacklistRepository = blacklistRepository;
        _logger = logger;

        Updates.CollectionChanged += OnUpdatesCollectionChanged;

        // When any code removes an entry from the blacklist the repository fires
        // BlacklistChanged. We listen here so items can be restored instantly without
        // a manual rescan whenever the user un-blacklists something in Settings.
        _blacklistRepository.BlacklistChanged += OnBlacklistChangedExternally;
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
        StatusMessage = L.T("StatusScanning");
        Updates.Clear();

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

            HasUpdates = Updates.Count > 0;
            HasScanned = true;

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
            StatusMessage = L.T("StatusScanFailed");
            _logger.Error("Programs scan encountered an unexpected error.", ex);
        }
        finally
        {
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

        StatusMessage = "Cancelling...";

        if (IsUpdateBatchRunning)
        {
            CurrentInstallDetailText = "Cancelling current Winget operation...";
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
            StatusMessage = "Select at least one application to update.";
            _logger.Info("Programs update requested with no selected applications.");
            return;
        }

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
                    SetCurrentInstallPhase("Installing...", null);
                    var progress = new Progress<InstallProgress>(OnInstallProgressReported);
                    var success = await _installer.InstallUpdateAsync(item, progress, _operationCts.Token);

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

            StatusMessage = $"Application updates completed. {succeededItems.Count} succeeded, {failedCount} failed.";
            _logger.Info($"Winget update batch completed. Total: {selectedItems.Count}, Succeeded: {succeededItems.Count}, Failed: {failedCount}.");
            batchCompletedCleanly = true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Application updates cancelled. {succeededItems.Count} succeeded, {failedCount} failed.";
            _logger.Info($"Winget update batch was cancelled. Completed before cancel: {succeededItems.Count + failedCount} of {selectedItems.Count}.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Update batch failed. See the log for details.";
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
            ? "Update complete. No application updates remaining."
            : $"Update complete. {Updates.Count} application update(s) still available.";

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
            StatusMessage = "No package IDs available to blacklist.";
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
                ? "Already blacklisted. Hidden from the list."
                : $"Hidden {candidateIds.Count} already-blacklisted item(s).";
            return 0;
        }

        StatusMessage = addedIds.Count == 1
            ? $"Added '{addedIds[0]}' to blacklist."
            : $"Added {addedIds.Count} package ID(s) to blacklist.";

        _logger.Info($"Programs page added {addedIds.Count} package ID(s) to blacklist.");
        return addedIds.Count;
    }

    private async Task ShowInstallingStateAsync(AppUpdateItem item, int currentIndex, int totalCount)
    {
        item.Status = UpdateStatus.Installing;
        CurrentBatchProgressText = $"Updating {currentIndex + 1} of {totalCount}";
        CurrentAppName = item.DisplayName;
        CurrentItemProgressPercent = 0;
        IsCurrentItemProgressIndeterminate = true;
        CurrentItemProgressText = "Current item progress: In progress...";
        SetCurrentInstallPhase("Preparing...", "Updating selected applications...");

        await Task.Yield();
    }

    private void OnInstallProgressReported(InstallProgress update)
    {
        // Live download size, straight from winget (e.g. "12.4 MB / 84.0 MB"). Units are
        // locale-neutral, so this reads correctly in any language.
        var hasDownloadText = !string.IsNullOrEmpty(update.DownloadText);
        if (hasDownloadText)
        {
            CurrentItemProgressText = update.DownloadText!;
        }

        if (update.Percent is > 0 and < 100)
        {
            CurrentItemProgressPercent = update.Percent;
            IsCurrentItemProgressIndeterminate = false;
            SetCurrentInstallPhase(hasDownloadText ? "Downloading..." : "Installing...", null);
        }
        else if (update.Percent >= 100)
        {
            CurrentItemProgressPercent = 0;
            IsCurrentItemProgressIndeterminate = true;
            if (!hasDownloadText)
            {
                CurrentItemProgressText = "Finalizing...";
            }
            SetCurrentInstallPhase("Finalizing...", null);
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

        StatusMessage = $"Restored {toRestore.Count} item(s) from blacklist.";

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
        CurrentItemProgressText = "Current item progress: In progress...";
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
