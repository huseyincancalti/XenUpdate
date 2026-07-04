using System.Collections.Specialized;
using System.ComponentModel;
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
/// ViewModel for the Drivers page.
/// Handles driver update scans, selected install flow, result presentation, and busy-state feedback.
/// </summary>
public sealed partial class DriversViewModel : ObservableObject
{
    private readonly IDriverUpdateService _service;
    private readonly ISystemRestoreService _restoreService;
    private readonly ILoggerService _logger;

    private CancellationTokenSource? _operationCts;

    /// <summary>
    /// Tracks items currently subscribed to <see cref="OnUpdateItemPropertyChanged"/>.
    /// Needed because bulk collection resets do not provide OldItems/NewItems.
    /// </summary>
    private readonly List<DriverUpdateItem> _subscribedItems = new();

    /// <summary>
    /// The list of available driver updates displayed in the DataGrid.
    /// Uses <see cref="BulkObservableCollection{T}"/> so scan results swap in as a
    /// single Reset instead of N per-item Add events.
    /// </summary>
    public BulkObservableCollection<DriverUpdateItem> Updates { get; } = new();

    /// <summary>True while any operation (scan or install) is running. Drives command enable/disable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private bool _isBusy;

    /// <summary>True only while a driver scan (initial or post-install refresh) is running.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Short status message shown below the page content.</summary>
    [ObservableProperty]
    private string _statusMessage = L.T("StatusDriversInitial");

    /// <summary>True after the user has completed at least one driver scan attempt.</summary>
    [ObservableProperty]
    private bool _hasScanned;

    /// <summary>True when the current driver result set contains one or more updates.</summary>
    [ObservableProperty]
    private bool _hasUpdates;

    /// <summary>True while a driver install batch is running.</summary>
    [ObservableProperty]
    private bool _isInstallBatchRunning;

    /// <summary>Shows the current item position within the install batch.</summary>
    [ObservableProperty]
    private string _currentBatchProgressText = string.Empty;

    /// <summary>Shows the currently active driver title.</summary>
    [ObservableProperty]
    private string _currentDriverTitle = string.Empty;

    /// <summary>Shows the manufacturer or device class of the active driver when available.</summary>
    [ObservableProperty]
    private string _currentDriverContextText = string.Empty;

    /// <summary>Shows the current install phase text for the active driver.</summary>
    [ObservableProperty]
    private string _currentInstallDetailText = string.Empty;

    /// <summary>True when the friendly empty-state panel should be shown.</summary>
    public bool IsEmptyStateVisible => HasScanned && !HasUpdates && !IsBusy;

    /// <summary>True when the current driver context line should be shown.</summary>
    public bool HasCurrentDriverContextText => !string.IsNullOrWhiteSpace(CurrentDriverContextText);

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
    /// Initializes the DriversViewModel with its required services.
    /// </summary>
    public DriversViewModel(IDriverUpdateService service, ISystemRestoreService restoreService, ILoggerService logger)
    {
        _service = service;
        _restoreService = restoreService;
        _logger = logger;

        Updates.CollectionChanged += OnUpdatesCollectionChanged;
    }

    /// <summary>
    /// Scans Windows Update for available driver updates.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ResetCancellation();
        _operationCts!.CancelAfter(TimeSpan.FromMinutes(5));
        ClearInstallFeedback();

        IsBusy = true;
        IsScanning = true;
        HasScanned = false;
        HasUpdates = false;
        Updates.Clear();
        StatusMessage = L.T("StatusScanningSlow");

        try
        {
            var results = await _service.GetAvailableUpdatesAsync(_operationCts!.Token);

            foreach (var item in results)
            {
                item.Status = UpdateStatus.Pending;
            }
            Updates.ReplaceAll(results);

            HasUpdates = Updates.Count > 0;
            HasScanned = true;

            StatusMessage = HasUpdates
                ? string.Format(L.T("StatusUpdatesAvailable"), Updates.Count)
                : L.T("DriversEmptyTitle");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L.T("StatusScanCancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("StatusScanFailed");
            _logger.Error("Driver update scan failed.", ex);
        }
        finally
        {
            IsBusy = false;
            IsScanning = false;
            NotifyVisibilityPropertiesChanged();
            ScanCommand.NotifyCanExecuteChanged();
            InstallSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => !IsBusy;

    /// <summary>
    /// Installs all selected driver updates one by one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallSelected))]
    private async Task InstallSelectedAsync()
    {
        if (IsBusy)
        {
            _logger.Warning("Driver install request ignored because another operation is already running.");
            return;
        }

        var selectedUpdates = Updates.Where(update => update.IsSelected).ToList();
        if (selectedUpdates.Count == 0)
        {
            StatusMessage = L.T("SelectAtLeastOneDriver");
            _logger.Info("Driver install requested with no selected driver updates.");
            return;
        }

        ResetCancellation();

        IsBusy = true;
        IsInstallBatchRunning = true;
        _logger.Info("Driver install batch started.");
        _logger.Info($"{selectedUpdates.Count} driver update(s) selected for installation.");

        // Safety: take a system restore point before touching drivers so a bad driver can be
        // rolled back. Continue even if it fails (e.g. System Restore disabled), but say which.
        StatusMessage = L.T("CreatingRestorePoint");
        var restoreCreated = await _restoreService.CreateRestorePointAsync("XenUpdate driver update");
        _logger.Info(restoreCreated
            ? "System restore point created before driver install."
            : "Could not create a system restore point; continuing with driver install.");

        var succeededItems = new List<DriverUpdateItem>();
        var failedCount = 0;
        var rebootRequired = false;
        var batchCompletedCleanly = false;

        try
        {
            for (var index = 0; index < selectedUpdates.Count; index++)
            {
                _operationCts!.Token.ThrowIfCancellationRequested();

                var update = selectedUpdates[index];
                await ShowInstallingStateAsync(update, index, selectedUpdates.Count);

                try
                {
                    var result = await _service.InstallUpdateAsync(
                        update,
                        new Progress<int>(OnInstallProgressReported),
                        _operationCts.Token);

                    // Reason set before Status so the failure-details row renders with
                    // its text in place. WUA does not surface a per-driver reason, so a
                    // generic pointer to the log is the honest message.
                    update.ErrorMessage = result.Succeeded ? null : L.T("InstallFailedGeneric");
                    update.Status = result.Succeeded ? UpdateStatus.Succeeded : UpdateStatus.Failed;
                    rebootRequired |= result.RebootRequired;

                    if (result.Succeeded)
                    {
                        succeededItems.Add(update);
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    update.Status = UpdateStatus.Pending;
                    throw;
                }
            }

            StatusMessage = rebootRequired
                ? string.Format(L.T("DriverInstallCompletedRestart"), succeededItems.Count, failedCount)
                : string.Format(L.T("DriverInstallCompleted"), succeededItems.Count, failedCount);

            CurrentInstallDetailText = rebootRequired
                ? L.T("InstallationCompletedRestartMayBeRequired")
                : L.T("InstallationCompletedSuccessfully");

            _logger.Info($"Driver install batch completed. Total: {selectedUpdates.Count}, Succeeded: {succeededItems.Count}, Failed: {failedCount}, RebootRequired: {rebootRequired}.");
            batchCompletedCleanly = true;
        }
        catch (OperationCanceledException)
        {
            CurrentInstallDetailText = L.T("InstallationCancelled");
            StatusMessage = string.Format(L.T("DriverInstallCancelled"), succeededItems.Count, failedCount);
            _logger.Info($"Driver install batch was cancelled. Completed before cancel: {succeededItems.Count + failedCount} of {selectedUpdates.Count}.");
        }
        catch (Exception ex)
        {
            CurrentInstallDetailText = L.T("InstallationFailedGeneric");
            StatusMessage = L.T("DriverInstallFailedGeneric");
            _logger.Error("Driver install batch failed.", ex);
        }
        finally
        {
            IsBusy = false;
            IsScanning = false;
            ScanCommand.NotifyCanExecuteChanged();
            InstallSelectedCommand.NotifyCanExecuteChanged();
            ClearInstallFeedbackAfterCompletion();
        }

        if (batchCompletedCleanly && succeededItems.Count > 0)
        {
            await RemoveSucceededItemsLocallyAsync(succeededItems, rebootRequired);
        }
    }

    private bool CanInstallSelected() => !IsBusy && Updates.Any(update => update.IsSelected);

    /// <summary>Cancels the currently running scan or install batch.</summary>
    [RelayCommand]
    private void CancelScan()
    {
        if (!IsBusy || _operationCts is null)
        {
            return;
        }

        StatusMessage = L.T("Cancelling");

        if (IsInstallBatchRunning)
        {
            CurrentInstallDetailText = L.T("CancellingDriverOp");
        }

        _operationCts.Cancel();
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

    partial void OnCurrentDriverContextTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasCurrentDriverContextText));
    }

    private async Task ShowInstallingStateAsync(DriverUpdateItem update, int currentIndex, int totalCount)
    {
        update.Status = UpdateStatus.Installing;
        CurrentBatchProgressText = string.Format(L.T("InstallingXOfY"), currentIndex + 1, totalCount);
        CurrentDriverTitle = update.DisplayName;
        CurrentDriverContextText = BuildDriverContextText(update);
        CurrentInstallDetailText = L.T("PreparingDownload");
        StatusMessage = CurrentBatchProgressText;

        await Task.Yield();
    }

    private void OnInstallProgressReported(int percent)
    {
        if (percent <= 25)
        {
            CurrentInstallDetailText = L.T("PreparingDownload");
        }
        else if (percent < 75)
        {
            CurrentInstallDetailText = L.T("DownloadingAndInstalling");
        }
        else
        {
            CurrentInstallDetailText = L.T("FinalizingInstallation");
        }
    }

    private void OnUpdatesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // ReplaceAll and Clear raise Reset, where OldItems/NewItems are null.
            // Rebuild subscriptions from the current collection so checkbox changes
            // immediately refresh InstallSelectedCommand.CanExecute.
            UnsubscribeAllItems();
            SubscribeAllItems();
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<DriverUpdateItem>())
                {
                    item.PropertyChanged -= OnUpdateItemPropertyChanged;
                    _subscribedItems.Remove(item);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<DriverUpdateItem>())
                {
                    item.PropertyChanged += OnUpdateItemPropertyChanged;
                    _subscribedItems.Add(item);
                }
            }
        }

        InstallSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AreAllSelected));
    }

    /// <summary>Unsubscribes from every tracked driver item and clears the tracking list.</summary>
    private void UnsubscribeAllItems()
    {
        foreach (var item in _subscribedItems)
        {
            item.PropertyChanged -= OnUpdateItemPropertyChanged;
        }

        _subscribedItems.Clear();
    }

    /// <summary>Subscribes to every item currently visible in <see cref="Updates"/>.</summary>
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
        if (e.PropertyName == nameof(DriverUpdateItem.IsSelected))
        {
            InstallSelectedCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AreAllSelected));
        }
    }

    private void ResetCancellation()
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
    }

    /// <summary>
    /// Waits briefly so the user can see the final <c>Succeeded</c> row states,
    /// then quietly removes successfully installed drivers from the visible list.
    /// No full rescan is performed; the user can press 'Scan for Driver Updates'
    /// to re-verify when they want.
    /// </summary>
    private async Task RemoveSucceededItemsLocallyAsync(List<DriverUpdateItem> succeededItems, bool rebootRequired)
    {
        // Short pause so the Succeeded state is visible before the row disappears.
        await Task.Delay(TimeSpan.FromSeconds(2));

        // If the user kicked off another operation during the pause, skip silently.
        if (IsBusy)
        {
            _logger.Info("Driver local removal skipped because another operation started during the pause.");
            return;
        }

        foreach (var item in succeededItems)
        {
            Updates.Remove(item);
        }

        HasUpdates = Updates.Count > 0;
        NotifyVisibilityPropertiesChanged();
        InstallSelectedCommand.NotifyCanExecuteChanged();

        StatusMessage = BuildInstallCompleteStatusMessage(rebootRequired);

        _logger.Info($"Driver removed {succeededItems.Count} succeeded row(s) from the visible list.");
    }

    private string BuildInstallCompleteStatusMessage(bool rebootRequired)
    {
        if (Updates.Count == 0)
        {
            return rebootRequired
                ? L.T("DriverCompleteNoneRestart")
                : L.T("DriverCompleteNone");
        }

        return rebootRequired
            ? string.Format(L.T("DriverCompleteRemainingRestart"), Updates.Count)
            : string.Format(L.T("DriverCompleteRemaining"), Updates.Count);
    }

    private void NotifyVisibilityPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    private void ClearInstallFeedback()
    {
        IsInstallBatchRunning = false;
        CurrentBatchProgressText = string.Empty;
        CurrentDriverTitle = string.Empty;
        CurrentDriverContextText = string.Empty;
        CurrentInstallDetailText = string.Empty;
    }

    private void ClearInstallFeedbackAfterCompletion()
    {
        IsInstallBatchRunning = false;
        CurrentBatchProgressText = string.Empty;
        CurrentDriverTitle = string.Empty;
        CurrentDriverContextText = string.Empty;
        CurrentInstallDetailText = string.Empty;
    }

    private static string BuildDriverContextText(DriverUpdateItem update)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(update.Manufacturer))
        {
            parts.Add($"Manufacturer: {update.Manufacturer}");
        }

        if (!string.IsNullOrWhiteSpace(update.DeviceClass))
        {
            parts.Add($"Device class: {update.DeviceClass}");
        }

        return string.Join(" | ", parts);
    }
}
