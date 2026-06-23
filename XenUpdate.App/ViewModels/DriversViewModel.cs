using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XenUpdate.App.Collections;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// ViewModel for the Drivers page.
/// Handles driver update scans, selected install flow, result presentation, and busy-state feedback.
/// </summary>
public sealed partial class DriversViewModel : ObservableObject
{
    private const string NoDriverUpdatesMessage = "No driver updates found. Your devices look current for now.";

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
    private string _statusMessage = "Press 'Scan for Driver Updates' to start.";

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
        ClearInstallFeedback();

        IsBusy = true;
        IsScanning = true;
        HasScanned = false;
        HasUpdates = false;
        Updates.Clear();
        StatusMessage = "Scanning for driver updates... This may take up to two minutes.";

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
                ? $"{Updates.Count} driver update(s) available."
                : NoDriverUpdatesMessage;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Driver update scan failed. See the log for details.";
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
            StatusMessage = "Select at least one driver update to install.";
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
        StatusMessage = "Creating a system restore point before installing drivers...";
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
                ? $"Driver installation completed. {succeededItems.Count} succeeded, {failedCount} failed. A restart may be required."
                : $"Driver installation completed. {succeededItems.Count} succeeded, {failedCount} failed.";

            CurrentInstallDetailText = rebootRequired
                ? "Installation completed. A restart may be required."
                : "Installation completed successfully.";

            _logger.Info($"Driver install batch completed. Total: {selectedUpdates.Count}, Succeeded: {succeededItems.Count}, Failed: {failedCount}, RebootRequired: {rebootRequired}.");
            batchCompletedCleanly = true;
        }
        catch (OperationCanceledException)
        {
            CurrentInstallDetailText = "Installation cancelled.";
            StatusMessage = $"Driver installation cancelled. {succeededItems.Count} succeeded, {failedCount} failed.";
            _logger.Info($"Driver install batch was cancelled. Completed before cancel: {succeededItems.Count + failedCount} of {selectedUpdates.Count}.");
        }
        catch (Exception ex)
        {
            CurrentInstallDetailText = "Installation failed.";
            StatusMessage = "Driver installation failed. See the log for details.";
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

        StatusMessage = "Cancelling...";

        if (IsInstallBatchRunning)
        {
            CurrentInstallDetailText = "Cancelling current driver operation...";
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
        CurrentBatchProgressText = $"Installing {currentIndex + 1} of {totalCount}...";
        CurrentDriverTitle = update.DisplayName;
        CurrentDriverContextText = BuildDriverContextText(update);
        CurrentInstallDetailText = "Preparing download...";
        StatusMessage = CurrentBatchProgressText;

        await Task.Yield();
    }

    private void OnInstallProgressReported(int percent)
    {
        if (percent <= 25)
        {
            CurrentInstallDetailText = "Preparing download...";
        }
        else if (percent < 75)
        {
            CurrentInstallDetailText = "Downloading and installing...";
        }
        else
        {
            CurrentInstallDetailText = "Finalizing installation...";
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
                ? "Installation complete. No remaining driver updates. A restart may be required."
                : "Installation complete. No remaining driver updates.";
        }

        return rebootRequired
            ? $"Installation complete. {Updates.Count} driver update(s) still available. A restart may be required."
            : $"Installation complete. {Updates.Count} driver update(s) still available.";
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
