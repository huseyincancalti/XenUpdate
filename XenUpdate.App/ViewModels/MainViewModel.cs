using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using XenUpdate.App.Messages;
using XenUpdate.App.Services;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IEnumerable<IUpdateProvider> _providers;
    private readonly IBlacklistRepository _blacklistRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILoggerService _logger;
    private readonly IHardwareScannerService _hardwareScannerService;
    private readonly IThemeService _themeService;
    private readonly ISystemRestoreService _systemRestoreService;
    private readonly IAppUpdateService _appUpdateService;

    private CancellationTokenSource? _installCancellationTokenSource;
    private string _appUpdateUrl = string.Empty;

    public ObservableCollection<CategorizedUpdateItem> Apps { get; } = new();

    public ObservableCollection<CategorizedUpdateItem> System { get; } = new();

    public ObservableCollection<CategorizedUpdateItem> Drivers { get; } = new();

    public ObservableCollection<CategorizedUpdateItem> UpdateQueue { get; } = new();

    [ObservableProperty]
    private HardwareProfile hardwareProfile = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveToQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartInstallQueueCommand))]
    [NotifyPropertyChangedFor(nameof(HasNoUpdates))]
    private bool isLoading;

    [ObservableProperty]
    private AppSettings settings = new();

    [ObservableProperty]
    private bool isDarkMode = true;

    [ObservableProperty]
    private ObservableCollection<BlacklistEntry> blacklistedItems = new();

    [ObservableProperty]
    private string newBlacklistId = string.Empty;

    [ObservableProperty]
    private bool isAllAppsSelected = true;

    [ObservableProperty]
    private bool isAllSystemSelected = true;

    [ObservableProperty]
    private bool isAllDriversSelected = true;

    [ObservableProperty]
    private int selectedUpdateCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelInstallationCommand))]
    private bool isInstalling;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedUpdatesCommand))]
    private bool hasFailedUpdates;

    [ObservableProperty]
    private bool hasAppUpdate;

    [ObservableProperty]
    private string selectedLanguage = "en";

    public List<string> AvailableLanguages { get; } = new() { "en", "tr" };

    public bool HasNoUpdates => !IsLoading && Apps.Count == 0 && System.Count == 0 && Drivers.Count == 0;

    public Action? RequestShowWindow { get; set; }
    public Action? RequestCloseApp { get; set; }

    [RelayCommand]
    private void ShowWindow() => RequestShowWindow?.Invoke();

    [RelayCommand]
    private void ExitApplication() => RequestCloseApp?.Invoke();

    [RelayCommand]
    private void DownloadAppUpdate()
    {
        if (string.IsNullOrWhiteSpace(_appUpdateUrl))
        {
            return;
        }

        global::System.Diagnostics.Process.Start(
            new global::System.Diagnostics.ProcessStartInfo(_appUpdateUrl)
            {
                UseShellExecute = true
            });
    }

    public MainViewModel(
        IEnumerable<IUpdateProvider> providers,
        IBlacklistRepository blacklistRepository,
        ISettingsRepository settingsRepository,
        ILoggerService logger,
        IHardwareScannerService hardwareScannerService,
        IThemeService themeService,
        ISystemRestoreService systemRestoreService,
        IAppUpdateService appUpdateService)
    {
        _providers = providers;
        _blacklistRepository = blacklistRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
        _hardwareScannerService = hardwareScannerService;
        _themeService = themeService;
        _systemRestoreService = systemRestoreService;
        _appUpdateService = appUpdateService;

        _ = LoadHardwareProfileAsync();
        _ = LoadSettingsAsync();
        _ = CheckForAppUpdateAsync();
    }

    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            var currentVersion = global::System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "0.0.0.0";

            var result = await _appUpdateService
                .CheckForAppUpdatesAsync(currentVersion)
                .ConfigureAwait(false);

            var hasUpdate = result.HasUpdate;
            var url = result.UpdateUrl;

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

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        AppLogger.AddLog("Starting update scan...");
        IsLoading = true;
        Apps.Clear();
        System.Clear();
        Drivers.Clear();

        try
        {
            var blacklist = await _blacklistRepository.GetEntriesAsync();
            var blacklistedValues = blacklist
                .Select(entry => entry.PackageId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var providerResults = await Task.WhenAll(
                _providers.Select(provider => provider.GetUpdatesAsync()));

            foreach (var item in providerResults
                         .SelectMany(items => items)
                         .Where(item => !IsBlacklisted(item, blacklistedValues)))
            {
                item.IsSelected = true;

                switch (item.Category)
                {
                    case UpdateCategory.Apps:
                        Apps.Add(item);
                        break;
                    case UpdateCategory.System:
                        System.Add(item);
                        break;
                    case UpdateCategory.Drivers:
                        Drivers.Add(item);
                        break;
                }
            }
        }
        finally
        {
            IsLoading = false;
            UpdateSelectionState();
            OnPropertyChanged(nameof(HasNoUpdates));

            var total = Apps.Count + System.Count + Drivers.Count;
            WeakReferenceMessenger.Default.Send(
                new NotificationMessage($"{total} {LocalizationManager.Instance["UpdatesFound"]}"));

            if (App.IsBackgroundStartup && !HasNoUpdates)
            {
                WeakReferenceMessenger.Default.Send(new BalloonTipMessage(
                    "XenUpdate",
                    $"Found {SelectedUpdateCount} update{(SelectedUpdateCount == 1 ? "" : "s")} available. Click to open."));
            }
        }
    }

    [RelayCommand]
    private async Task IgnoreUpdateAsync(CategorizedUpdateItem? item)
    {
        if (item is null)
        {
            return;
        }

        var blacklistValue = !string.IsNullOrWhiteSpace(item.Id)
            ? item.Id
            : item.Name;

        if (!string.IsNullOrWhiteSpace(blacklistValue))
        {
            await _blacklistRepository.AddAsync(blacklistValue, "Ignored from main update list.");
            WeakReferenceMessenger.Default.Send(
                new NotificationMessage($"'{item.Name}' {LocalizationManager.Instance["AddedToBlacklist"]}"));
        }

        UpdateQueue.Remove(item);
        RemoveFromSourceCollection(item);
    }

    [RelayCommand]
    private void CopyLogsToClipboard()
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XenUpdate", "logs.txt");

            if (!File.Exists(logPath))
            {
                return;
            }

            var logContent = File.ReadAllText(logPath);
            if (!string.IsNullOrWhiteSpace(logContent))
            {
                global::System.Windows.Clipboard.SetText(logContent);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to copy logs to clipboard.", ex);
        }
    }

    public Action? RequestOpenLogViewer { get; set; }

    [RelayCommand]
    private void OpenLogViewer()
    {
        AppLogger.AddLog("Log viewer opened.");
        RequestOpenLogViewer?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanMoveToQueue))]
    private void MoveToQueue()
    {
        UpdateQueue.Clear();

        var selectedUpdates = Apps
            .Concat(System)
            .Concat(Drivers)
            .Where(item => item.IsSelected)
            .ToList();

        foreach (var item in selectedUpdates)
        {
            UpdateQueue.Add(item);
        }

        SortUpdateQueue();
        StartInstallQueueCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveToQueue() => !IsLoading;

    [RelayCommand]
    private void RemoveFromQueue(CategorizedUpdateItem? item)
    {
        if (item is null || !UpdateQueue.Remove(item))
        {
            return;
        }

        item.IsSelected = false;
        StartInstallQueueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartInstallQueue))]
    private async Task StartInstallQueueAsync()
    {
        if (UpdateQueue.Count == 0)
        {
            return;
        }

        IsLoading = true;
        IsInstalling = true;

        _installCancellationTokenSource?.Dispose();
        _installCancellationTokenSource = new CancellationTokenSource();
        var token = _installCancellationTokenSource.Token;

        WeakReferenceMessenger.Default.Send(new NotificationMessage("Creating System Restore Point..."));
        await _systemRestoreService.CreateRestorePointAsync("XenUpdate Pre-Install");

        try
        {
            foreach (var item in UpdateQueue.ToList())
            {
                token.ThrowIfCancellationRequested();

                var provider = GetProviderForCategory(item.Category);
                if (provider is null)
                {
                    item.Status = UpdateStatus.Failed;
                    HasFailedUpdates = true;
                    continue;
                }

                item.Status = UpdateStatus.Installing;
                item.ProgressPercentage = 0;
                var progress = new Progress<double>(value =>
                    item.ProgressPercentage = Math.Clamp(value, 0, 100));

                try
                {
                    var succeeded = await provider.InstallUpdateAsync(item, progress);

                    if (succeeded)
                    {
                        item.ProgressPercentage = 100;
                        item.Status = UpdateStatus.Installed;
                        WeakReferenceMessenger.Default.Send(
                            new NotificationMessage($"{item.Name} {LocalizationManager.Instance["UpdateSuccess"]}"));
                        await Task.Delay(450, token);
                        UpdateQueue.Remove(item);
                        RemoveFromSourceCollection(item);
                    }
                    else
                    {
                        item.ProgressPercentage = 0;
                        item.Status = UpdateStatus.Failed;
                        HasFailedUpdates = true;
                        _logger.Info($"Installation returned failure for: {item.Name}");
                    }
                }
                catch (OperationCanceledException)
                {
                    item.Status = UpdateStatus.Pending;
                    throw;
                }
                catch (Exception ex)
                {
                    item.ProgressPercentage = 0;
                    item.Status = UpdateStatus.Failed;
                    HasFailedUpdates = true;
                    _logger.Info($"Error installing '{item.Name}': {ex.Message}");
                    WeakReferenceMessenger.Default.Send(
                        new NotificationMessage($"Failed to install {item.Name}."));
                }
            }

            if (UpdateQueue.Count == 0)
            {
                WeakReferenceMessenger.Default.Send(
                    new NotificationMessage("All updates completed."));
            }
        }
        catch (OperationCanceledException)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("Installation cancelled."));
            _logger.Info("Installation was cancelled by the user.");
        }
        finally
        {
            IsLoading = false;
            IsInstalling = false;
            HasFailedUpdates = UpdateQueue.Any(i => i.Status == UpdateStatus.Failed);
            StartInstallQueueCommand.NotifyCanExecuteChanged();
            CancelInstallationCommand.NotifyCanExecuteChanged();
            RetryFailedUpdatesCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStartInstallQueue() => !IsLoading && UpdateQueue.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCancelInstallation))]
    private void CancelInstallation()
    {
        _installCancellationTokenSource?.Cancel();
    }

    private bool CanCancelInstallation() => IsInstalling;

    [RelayCommand(CanExecute = nameof(CanRetryFailedUpdates))]
    private async Task RetryFailedUpdatesAsync()
    {
        var failedItems = UpdateQueue
            .Where(item => item.Status == UpdateStatus.Failed)
            .ToList();

        if (failedItems.Count == 0)
        {
            return;
        }

        foreach (var item in failedItems)
        {
            item.Status = UpdateStatus.Pending;
            item.ProgressPercentage = 0;
        }

        HasFailedUpdates = false;
        await StartInstallQueueAsync();
    }

    private bool CanRetryFailedUpdates() => HasFailedUpdates && !IsInstalling;

    private IUpdateProvider? GetProviderForCategory(UpdateCategory category)
    {
        return category switch
        {
            UpdateCategory.Apps => _providers.FirstOrDefault(provider =>
                provider.GetType().Name.Contains("Winget", StringComparison.OrdinalIgnoreCase)),
            UpdateCategory.System or UpdateCategory.Drivers => _providers.FirstOrDefault(provider =>
                provider.GetType().Name.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
    }

    private void RemoveFromSourceCollection(CategorizedUpdateItem item)
    {
        _ = item.Category switch
        {
            UpdateCategory.Apps => Apps.Remove(item),
            UpdateCategory.System => System.Remove(item),
            UpdateCategory.Drivers => Drivers.Remove(item),
            _ => false
        };
    }

    private void AddToSourceCollection(CategorizedUpdateItem item)
    {
        var targetCollection = item.Category switch
        {
            UpdateCategory.Apps => Apps,
            UpdateCategory.System => System,
            UpdateCategory.Drivers => Drivers,
            _ => null
        };

        if (targetCollection is not null && !targetCollection.Contains(item))
        {
            targetCollection.Add(item);
        }
    }

    private void SortUpdateQueue()
    {
        var sortedItems = UpdateQueue
            .OrderBy(item => GetQueueSortOrder(item.Category))
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        UpdateQueue.Clear();

        foreach (var item in sortedItems)
        {
            UpdateQueue.Add(item);
        }
    }

    private static int GetQueueSortOrder(UpdateCategory category)
    {
        return category switch
        {
            UpdateCategory.System => 0,
            UpdateCategory.Drivers => 1,
            UpdateCategory.Apps => 2,
            _ => 3
        };
    }

    private async Task LoadHardwareProfileAsync()
    {
        HardwareProfile = await _hardwareScannerService.GetCurrentHardwareAsync();
    }

    private async Task LoadSettingsAsync()
    {
        Settings = await _settingsRepository.LoadAsync();
        IsDarkMode = Settings.Theme != AppTheme.Light;

        SelectedLanguage = Settings.Language ?? "en";
        Services.LocalizationManager.Instance.ChangeLanguage(SelectedLanguage);

        if (Settings.ScanOnStartup)
        {
            CheckForUpdatesCommand.Execute(null);
        }
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        Services.LocalizationManager.Instance.ChangeLanguage(value);
        Settings.Language = value;
        _ = _settingsRepository.SaveAsync(Settings);
    }

    partial void OnSettingsChanging(AppSettings value)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    partial void OnSettingsChanged(AppSettings value)
    {
        value.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = _settingsRepository.SaveAsync(Settings);
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        var theme = value ? AppTheme.Dark : AppTheme.Light;
        _themeService.ApplyTheme(theme);
        Settings.Theme = theme;
        _ = _settingsRepository.SaveAsync(Settings);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    [RelayCommand]
    private void ToggleSelection(CategorizedUpdateItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;

        if (item.IsSelected)
        {
            WeakReferenceMessenger.Default.Send(new UpdateSelectedMessage());
        }

        UpdateSelectionState();
    }

    [RelayCommand]
    private void ToggleSelectAll(string? category)
    {
        var collection = category switch
        {
            "Apps" => Apps,
            "System" => System,
            "Drivers" => Drivers,
            _ => null
        };

        if (collection is null || collection.Count == 0)
        {
            return;
        }

        var currentAllSelected = collection.All(item => item.IsSelected);
        var targetState = !currentAllSelected;
        foreach (var item in collection)
        {
            item.IsSelected = targetState;
        }

        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        IsAllAppsSelected = Apps.Count > 0 && Apps.All(i => i.IsSelected);
        IsAllSystemSelected = System.Count > 0 && System.All(i => i.IsSelected);
        IsAllDriversSelected = Drivers.Count > 0 && Drivers.All(i => i.IsSelected);

        SelectedUpdateCount = Apps.Count(i => i.IsSelected)
                            + System.Count(i => i.IsSelected)
                            + Drivers.Count(i => i.IsSelected);
    }

    [RelayCommand]
    private async Task OpenBlacklistManagerAsync()
    {
        try
        {
            var entries = await _blacklistRepository.GetEntriesAsync();
            BlacklistedItems.Clear();
            foreach (var entry in entries)
            {
                BlacklistedItems.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load blacklist entries.", ex);
        }
    }

    [RelayCommand]
    private async Task RemoveFromBlacklistAsync(BlacklistEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            await _blacklistRepository.RemoveAsync(entry.PackageId);
            BlacklistedItems.Remove(entry);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove '{entry.PackageId}' from blacklist.", ex);
        }
    }

    [RelayCommand]
    private async Task AddManualBlacklistAsync()
    {
        var id = NewBlacklistId.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        try
        {
            await _blacklistRepository.AddAsync(id, "Added manually.");
            var entries = await _blacklistRepository.GetEntriesAsync();
            BlacklistedItems.Clear();
            foreach (var entry in entries)
            {
                BlacklistedItems.Add(entry);
            }
            NewBlacklistId = string.Empty;
            WeakReferenceMessenger.Default.Send(
                new NotificationMessage($"'{id}' {LocalizationManager.Instance["AddedToBlacklist"]}"));
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to add '{id}' to blacklist.", ex);
        }
    }

    private static bool IsBlacklisted(CategorizedUpdateItem item, HashSet<string> blacklistedValues)
    {
        return (!string.IsNullOrWhiteSpace(item.Id) && blacklistedValues.Contains(item.Id))
               || (!string.IsNullOrWhiteSpace(item.Name) && blacklistedValues.Contains(item.Name));
    }
}
