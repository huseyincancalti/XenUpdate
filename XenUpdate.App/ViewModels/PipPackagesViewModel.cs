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
/// ViewModel for the Python Packages page.
/// Drives the pip scan flow, selected install flow, displays outdated packages, and manages busy state.
/// </summary>
public sealed partial class PipPackagesViewModel : ObservableObject
{
    private readonly IPipScanner _scanner;
    private readonly IPipInstaller _installer;
    private readonly ILoggerService _logger;

    private CancellationTokenSource? _operationCts;

    /// <summary>
    /// Tracks items currently subscribed to <see cref="OnUpdateItemPropertyChanged"/>.
    /// Needed because bulk collection resets do not provide OldItems/NewItems.
    /// </summary>
    private readonly List<PipPackageItem> _subscribedItems = new();

    /// <summary>
    /// The list of outdated packages shown in the DataGrid.
    /// Uses <see cref="BulkObservableCollection{T}"/> so scan results swap in as a
    /// single Reset instead of N per-item Add events.
    /// </summary>
    public BulkObservableCollection<PipPackageItem> Updates { get; } = new();

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
    private string _statusMessage = L.T("StatusPipPackagesInitial");

    /// <summary>True after the user has completed at least one scan attempt.</summary>
    [ObservableProperty]
    private bool _hasScanned;

    /// <summary>True when the current result set contains one or more outdated packages.</summary>
    [ObservableProperty]
    private bool _hasUpdates;

    /// <summary>True while a pip install batch is running.</summary>
    [ObservableProperty]
    private bool _isInstallBatchRunning;

    /// <summary>Shows the current item position within the install batch.</summary>
    [ObservableProperty]
    private string _currentBatchProgressText = string.Empty;

    /// <summary>Shows the package currently being installed.</summary>
    [ObservableProperty]
    private string _currentPackageName = string.Empty;

    /// <summary>Shows the current install phase text for the active package.</summary>
    [ObservableProperty]
    private string _currentInstallDetailText = string.Empty;

    /// <summary>True when the empty-state placeholder should be shown instead of the grid.</summary>
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

    /// <summary>Initializes the ViewModel with its required services.</summary>
    public PipPackagesViewModel(IPipScanner scanner, IPipInstaller installer, ILoggerService logger)
    {
        _scanner = scanner;
        _installer = installer;
        _logger = logger;

        Updates.CollectionChanged += OnUpdatesCollectionChanged;
    }

    /// <summary>Scans for outdated pip packages in the background.</summary>
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
        StatusMessage = L.T("StatusScanning");

        try
        {
            var results = await _scanner.GetAvailableUpdatesAsync(_operationCts!.Token);

            foreach (var item in results)
            {
                item.Status = UpdateStatus.Pending;
            }
            Updates.ReplaceAll(results);

            HasUpdates = Updates.Count > 0;
            HasScanned = true;

            StatusMessage = HasUpdates
                ? string.Format(L.T("StatusUpdatesAvailable"), Updates.Count)
                : L.T("PipPackagesEmptyTitle");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L.T("StatusScanCancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("StatusScanFailed");
            _logger.Error("Pip scan failed.", ex);
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

    /// <summary>Installs all selected package updates one by one.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallSelected))]
    private async Task InstallSelectedAsync()
    {
        if (IsBusy)
        {
            _logger.Warning("Pip install request ignored because another operation is already running.");
            return;
        }

        var selectedUpdates = Updates.Where(update => update.IsSelected).ToList();
        if (selectedUpdates.Count == 0)
        {
            StatusMessage = L.T("SelectAtLeastOnePackage");
            _logger.Info("Pip install requested with no selected packages.");
            return;
        }

        ResetCancellation();

        IsBusy = true;
        IsInstallBatchRunning = true;
        _logger.Info("Pip install batch started.");
        _logger.Info($"{selectedUpdates.Count} package(s) selected for update.");

        var succeededItems = new List<PipPackageItem>();
        var failedCount = 0;
        var batchCompletedCleanly = false;

        try
        {
            for (var index = 0; index < selectedUpdates.Count; index++)
            {
                _operationCts!.Token.ThrowIfCancellationRequested();

                var item = selectedUpdates[index];
                await ShowInstallingStateAsync(item, index, selectedUpdates.Count);

                try
                {
                    string? failureReason = null;
                    var progress = new Progress<InstallProgress>(update =>
                    {
                        if (update.FailureReason is not null)
                        {
                            failureReason = update.FailureReason;
                            return;
                        }
                        OnInstallProgressReported(update.Percent);
                    });

                    var success = await _installer.InstallUpdateAsync(item, progress, _operationCts.Token);

                    // Pip's failure text is already display-ready (see PipInstaller) —
                    // no localization key lookup needed here, unlike winget's fixed code set.
                    item.ErrorMessage = success ? null : failureReason ?? L.T("InstallFailedGeneric");
                    item.Status = success ? UpdateStatus.Succeeded : UpdateStatus.Failed;

                    if (success)
                    {
                        succeededItems.Add(item);
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    item.Status = UpdateStatus.Pending;
                    throw;
                }
            }

            StatusMessage = string.Format(L.T("PipInstallCompleted"), succeededItems.Count, failedCount);
            CurrentInstallDetailText = L.T("InstallationCompletedSuccessfully");
            _logger.Info($"Pip install batch completed. Total: {selectedUpdates.Count}, Succeeded: {succeededItems.Count}, Failed: {failedCount}.");
            batchCompletedCleanly = true;
        }
        catch (OperationCanceledException)
        {
            CurrentInstallDetailText = L.T("InstallationCancelled");
            StatusMessage = string.Format(L.T("PipInstallCancelled"), succeededItems.Count, failedCount);
            _logger.Info($"Pip install batch was cancelled. Completed before cancel: {succeededItems.Count + failedCount} of {selectedUpdates.Count}.");
        }
        catch (Exception ex)
        {
            CurrentInstallDetailText = L.T("InstallationFailedGeneric");
            StatusMessage = L.T("UpdateBatchFailed");
            _logger.Error("Pip install batch failed.", ex);
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
            await RemoveSucceededItemsLocallyAsync(succeededItems);
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
            CurrentInstallDetailText = L.T("CancellingPipOp");
        }

        _operationCts.Cancel();
    }

    partial void OnHasScannedChanged(bool value) => OnPropertyChanged(nameof(IsEmptyStateVisible));
    partial void OnHasUpdatesChanged(bool value) => OnPropertyChanged(nameof(IsEmptyStateVisible));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmptyStateVisible));

    private async Task ShowInstallingStateAsync(PipPackageItem item, int currentIndex, int totalCount)
    {
        item.Status = UpdateStatus.Installing;
        CurrentBatchProgressText = string.Format(L.T("InstallingXOfY"), currentIndex + 1, totalCount);
        CurrentPackageName = item.DisplayName;
        CurrentInstallDetailText = L.T("PreparingDownload");
        StatusMessage = CurrentBatchProgressText;

        await Task.Yield();
    }

    private void OnInstallProgressReported(int percent)
    {
        CurrentInstallDetailText = percent switch
        {
            <= 25 => L.T("PreparingDownload"),
            < 75 => L.T("DownloadingAndInstalling"),
            _ => L.T("FinalizingInstallation")
        };
    }

    private void OnUpdatesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            UnsubscribeAllItems();
            SubscribeAllItems();
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<PipPackageItem>())
                {
                    item.PropertyChanged -= OnUpdateItemPropertyChanged;
                    _subscribedItems.Remove(item);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<PipPackageItem>())
                {
                    item.PropertyChanged += OnUpdateItemPropertyChanged;
                    _subscribedItems.Add(item);
                }
            }
        }

        InstallSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AreAllSelected));
    }

    private void UnsubscribeAllItems()
    {
        foreach (var item in _subscribedItems)
        {
            item.PropertyChanged -= OnUpdateItemPropertyChanged;
        }
        _subscribedItems.Clear();
    }

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
        if (e.PropertyName == nameof(PipPackageItem.IsSelected))
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
    /// then quietly removes successfully updated packages from the visible list.
    /// </summary>
    private async Task RemoveSucceededItemsLocallyAsync(List<PipPackageItem> succeededItems)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));

        if (IsBusy)
        {
            _logger.Info("Pip local removal skipped because another operation started during the pause.");
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
            ? L.T("PipUpdateCompleteNone")
            : string.Format(L.T("PipUpdateCompleteRemaining"), Updates.Count);

        _logger.Info($"Pip removed {succeededItems.Count} succeeded row(s) from the visible list.");
    }

    private void NotifyVisibilityPropertiesChanged() => OnPropertyChanged(nameof(IsEmptyStateVisible));

    private void ClearInstallFeedback()
    {
        IsInstallBatchRunning = false;
        CurrentBatchProgressText = string.Empty;
        CurrentPackageName = string.Empty;
        CurrentInstallDetailText = string.Empty;
    }

    private void ClearInstallFeedbackAfterCompletion() => ClearInstallFeedback();
}
