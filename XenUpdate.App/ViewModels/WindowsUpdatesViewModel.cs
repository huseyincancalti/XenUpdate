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
/// ViewModel for the Windows Updates page.
/// Drives the WUA scan flow, selected install flow, displays available updates, and manages busy state.
/// </summary>
public sealed partial class WindowsUpdatesViewModel : ObservableObject
{
    private readonly IWindowsUpdateService _service;
    private readonly ILoggerService _logger;

    private CancellationTokenSource? _operationCts;

    /// <summary>
    /// Tracks items currently subscribed to <see cref="OnUpdateItemPropertyChanged"/>.
    /// Needed because bulk collection resets do not provide OldItems/NewItems.
    /// </summary>
    private readonly List<WindowsUpdateItem> _subscribedItems = new();

    /// <summary>
    /// The list of available OS updates shown in the DataGrid.
    /// Uses <see cref="BulkObservableCollection{T}"/> so scan results swap in as a
    /// single Reset instead of N per-item Add events.
    /// </summary>
    public BulkObservableCollection<WindowsUpdateItem> Updates { get; } = new();

    /// <summary>True while any operation (scan or install) is running. Drives command enable/disable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private bool _isBusy;

    /// <summary>True only while a scan (initial or post-install refresh) is running.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Short status line shown below the page content.</summary>
    [ObservableProperty]
    private string _statusMessage = L.T("StatusWindowsUpdatesInitial");

    /// <summary>True after the user has completed at least one scan attempt.</summary>
    [ObservableProperty]
    private bool _hasScanned;

    /// <summary>True when the current result set contains one or more updates.</summary>
    [ObservableProperty]
    private bool _hasUpdates;

    /// <summary>True while a Windows Update install batch is running.</summary>
    [ObservableProperty]
    private bool _isInstallBatchRunning;

    /// <summary>Shows the current item position within the install batch.</summary>
    [ObservableProperty]
    private string _currentBatchProgressText = string.Empty;

    /// <summary>Shows the currently active Windows update title.</summary>
    [ObservableProperty]
    private string _currentUpdateTitle = string.Empty;

    /// <summary>Shows the KB article of the currently active Windows update when available.</summary>
    [ObservableProperty]
    private string _currentKbArticle = string.Empty;

    /// <summary>Shows the current install phase text for the active update.</summary>
    [ObservableProperty]
    private string _currentInstallDetailText = string.Empty;

    /// <summary>
    /// True when the empty-state placeholder should be shown instead of the grid.
    /// </summary>
    public bool IsEmptyStateVisible => HasScanned && !HasUpdates && !IsBusy;

    /// <summary>
    /// True when the KB article line should be shown in the install feedback area.
    /// </summary>
    public bool HasCurrentKbArticle => !string.IsNullOrWhiteSpace(CurrentKbArticle);

    /// <summary>Initializes the ViewModel with its required services.</summary>
    public WindowsUpdatesViewModel(IWindowsUpdateService service, ILoggerService logger)
    {
        _service = service;
        _logger = logger;

        Updates.CollectionChanged += OnUpdatesCollectionChanged;
    }

    /// <summary>
    /// Searches for available Windows Updates in the background.
    /// The first run can take up to two minutes while WUA contacts Microsoft servers.
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
        HasUpdates = false;
        HasScanned = false;
        Updates.Clear();
        StatusMessage = L.T("StatusScanningSlow");

        try
        {
            var results = await _service.GetAvailableUpdatesAsync(_operationCts!.Token);

            // Continuation of `await` resumes on the UI thread (WPF sync context),
            // so we can mutate the observable collection directly here.
            foreach (var item in results)
            {
                item.Status = UpdateStatus.Pending;
            }
            Updates.ReplaceAll(results);

            HasUpdates = Updates.Count > 0;
            HasScanned = true;

            StatusMessage = HasUpdates
                ? string.Format(L.T("StatusUpdatesAvailable"), Updates.Count)
                : L.T("WindowsUpdatesEmptyTitle");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L.T("StatusScanCancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("StatusScanFailed");
            _logger.Error("Windows Update scan failed.", ex);
        }
        finally
        {
            IsBusy = false;
            IsScanning = false;
            NotifyVisibilityPropertiesChanged();
            ScanCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => !IsBusy;

    /// <summary>
    /// Installs all selected Windows updates one by one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallSelected))]
    private async Task InstallSelectedAsync()
    {
        if (IsBusy)
        {
            _logger.Warning("Windows Update install request ignored because another operation is already running.");
            return;
        }

        var selectedUpdates = Updates.Where(update => update.IsSelected).ToList();
        if (selectedUpdates.Count == 0)
        {
            StatusMessage = "Select at least one Windows update to install.";
            _logger.Info("Windows Update install requested with no selected updates.");
            return;
        }

        ResetCancellation();

        IsBusy = true;
        IsInstallBatchRunning = true;
        _logger.Info("Windows Update install batch started.");
        _logger.Info($"{selectedUpdates.Count} Windows update(s) selected for installation.");

        var succeededItems = new List<WindowsUpdateItem>();
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
                    _logger.Info($"Installing Windows update '{update.DisplayName}'.");

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

                    if (result.RebootRequired)
                    {
                        _logger.Info($"Windows update '{update.DisplayName}' may require a restart.");
                    }
                }
                catch (OperationCanceledException)
                {
                    update.Status = UpdateStatus.Pending;
                    throw;
                }
            }

            CurrentInstallDetailText = rebootRequired
                ? "Installation completed. A restart may be required."
                : "Installation completed successfully.";

            StatusMessage = rebootRequired
                ? $"Windows Update installation completed. {succeededItems.Count} succeeded, {failedCount} failed. A restart may be required."
                : $"Windows Update installation completed. {succeededItems.Count} succeeded, {failedCount} failed.";

            _logger.Info($"Windows Update install batch completed. Total: {selectedUpdates.Count}, Succeeded: {succeededItems.Count}, Failed: {failedCount}, RebootRequired: {rebootRequired}.");
            batchCompletedCleanly = true;
        }
        catch (OperationCanceledException)
        {
            CurrentInstallDetailText = "Installation cancelled.";
            StatusMessage = $"Windows Update installation cancelled. {succeededItems.Count} succeeded, {failedCount} failed.";
            _logger.Info($"Windows Update install batch was cancelled. Completed before cancel: {succeededItems.Count + failedCount} of {selectedUpdates.Count}.");
        }
        catch (Exception ex)
        {
            CurrentInstallDetailText = "Installation failed.";
            StatusMessage = "Installation failed. See the log for details.";
            _logger.Error("Windows Update install batch failed.", ex);
        }
        finally
        {
            IsBusy = false;
            IsScanning = false;
            InstallSelectedCommand.NotifyCanExecuteChanged();
            ScanCommand.NotifyCanExecuteChanged();
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
            CurrentInstallDetailText = "Cancelling current Windows Update operation...";
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

    partial void OnCurrentKbArticleChanged(string value)
    {
        OnPropertyChanged(nameof(HasCurrentKbArticle));
    }

    private async Task ShowInstallingStateAsync(WindowsUpdateItem update, int currentIndex, int totalCount)
    {
        update.Status = UpdateStatus.Installing;
        CurrentBatchProgressText = $"Installing {currentIndex + 1} of {totalCount}...";
        CurrentUpdateTitle = update.DisplayName;
        CurrentKbArticle = update.KbArticleId;
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
                foreach (var item in e.OldItems.OfType<WindowsUpdateItem>())
                {
                    item.PropertyChanged -= OnUpdateItemPropertyChanged;
                    _subscribedItems.Remove(item);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<WindowsUpdateItem>())
                {
                    item.PropertyChanged += OnUpdateItemPropertyChanged;
                    _subscribedItems.Add(item);
                }
            }
        }

        InstallSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Unsubscribes from every tracked update item and clears the tracking list.</summary>
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
        if (e.PropertyName == nameof(WindowsUpdateItem.IsSelected))
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
    /// then quietly removes successfully installed rows from the visible list.
    /// No full rescan is performed; the user can press 'Check for Updates' to
    /// re-verify when they want.
    /// </summary>
    private async Task RemoveSucceededItemsLocallyAsync(List<WindowsUpdateItem> succeededItems, bool rebootRequired)
    {
        // Short pause so the Succeeded state is visible before the row disappears.
        await Task.Delay(TimeSpan.FromSeconds(2));

        // If the user kicked off another operation during the pause, skip silently.
        if (IsBusy)
        {
            _logger.Info("Windows Update local removal skipped because another operation started during the pause.");
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

        _logger.Info($"Windows Update removed {succeededItems.Count} succeeded row(s) from the visible list.");
    }

    private string BuildInstallCompleteStatusMessage(bool rebootRequired)
    {
        if (Updates.Count == 0)
        {
            return rebootRequired
                ? "Installation complete. No remaining Windows updates. A restart may be required."
                : "Installation complete. No remaining Windows updates.";
        }

        return rebootRequired
            ? $"Installation complete. {Updates.Count} Windows update(s) still available. A restart may be required."
            : $"Installation complete. {Updates.Count} Windows update(s) still available.";
    }

    private void NotifyVisibilityPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    private void ClearInstallFeedback()
    {
        IsInstallBatchRunning = false;
        CurrentBatchProgressText = string.Empty;
        CurrentUpdateTitle = string.Empty;
        CurrentKbArticle = string.Empty;
        CurrentInstallDetailText = string.Empty;
    }

    private void ClearInstallFeedbackAfterCompletion()
    {
        IsInstallBatchRunning = false;
        CurrentBatchProgressText = string.Empty;
        CurrentUpdateTitle = string.Empty;
        CurrentKbArticle = string.Empty;
    }
}
