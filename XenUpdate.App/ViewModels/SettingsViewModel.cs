using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// ViewModel for the Settings page.
/// Manages user preferences and the blacklist.
/// Binds to <c>SettingsView.xaml</c>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly IBlacklistRepository _blacklistRepo;
    private readonly IWhitelistRepository _whitelistRepo;
    private readonly ILoggerService _logger;
    private readonly IThemeService _themeService;

    /// <summary>The currently loaded application settings, bound to the Settings form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    private AppSettings _settings = new();

    /// <summary>
    /// True when the current theme is <see cref="AppTheme.Light"/>.
    /// Used by DataTriggers in SettingsView so the toggle button always reflects
    /// the correct next-action icon without needing enum-string comparisons.
    /// </summary>
    public bool IsLightTheme => Settings.Theme == AppTheme.Light;

    /// <summary>The package ID entered for a new blacklist entry.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddBlacklistEntryCommand))]
    private string _newBlacklistPackageId = string.Empty;

    /// <summary>The optional reason entered for a new blacklist entry.</summary>
    [ObservableProperty]
    private string _newBlacklistReason = string.Empty;

    /// <summary>The currently selected blacklist entry in the UI.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedBlacklistEntryCommand))]
    private BlacklistEntry? _selectedBlacklistEntry;

    /// <summary>Short feedback text shown on the Settings page.</summary>
    [ObservableProperty]
    private string _statusMessage = "Settings loaded.";

    /// <summary>The list of blacklist entries shown in the Settings UI.</summary>
    public ObservableCollection<BlacklistEntry> BlacklistEntries { get; } = new();

    /// <summary>The currently selected whitelist entry in the UI.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedWhitelistEntryCommand))]
    private WhitelistEntry? _selectedWhitelistEntry;

    /// <summary>
    /// The list of whitelisted updates shown in the Settings UI. Entries are only ever added
    /// from a page's "Add to Auto-Update" context menu action (where the display name and
    /// source are known for certain) — there is no free-form add form here, unlike the
    /// blacklist, since typing an arbitrary ID here would have nothing to match against.
    /// </summary>
    public ObservableCollection<WhitelistEntry> WhitelistEntries { get; } = new();

    /// <summary>A selectable UI language: its code plus a display name.</summary>
    public sealed record LanguageOption(string Code, string Display);

    /// <summary>
    /// The languages the user can choose from in the dropdown. Discovered at runtime from the
    /// JSON files in <c>Assets/Locales</c>, so dropping in a new translated file adds a language
    /// with no code change.
    /// </summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        LocalizationManager.Instance.GetAvailableLanguages()
            .Select(l => new LanguageOption(l.Code, l.Name))
            .ToList();

    /// <summary>The active UI language code, bound to the language dropdown.</summary>
    [ObservableProperty]
    private string _selectedLanguageCode = "en";

    partial void OnSelectedLanguageCodeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        LocalizationManager.Instance.ChangeLanguage(value);

        if (!string.Equals(Settings.Language, value, StringComparison.OrdinalIgnoreCase))
        {
            Settings.Language = value;
            _ = SaveAsync();
        }
    }

    /// <summary>
    /// Initializes the SettingsViewModel with its required repositories.
    /// </summary>
    public SettingsViewModel(
        ISettingsRepository settingsRepo,
        IBlacklistRepository blacklistRepo,
        IWhitelistRepository whitelistRepo,
        ILoggerService logger,
        IThemeService themeService)
    {
        _settingsRepo = settingsRepo;
        _blacklistRepo = blacklistRepo;
        _whitelistRepo = whitelistRepo;
        _logger = logger;
        _themeService = themeService;

        // When ProgramsViewModel (or any other code) adds/removes blacklist entries,
        // the repository fires BlacklistChanged. We refresh the visible list so the
        // Settings page always stays in sync without a manual reload.
        _blacklistRepo.BlacklistChanged += OnBlacklistChangedExternally;

        // Same idea for the whitelist: any page's context menu can add/remove an entry,
        // so this page reloads its list whenever that happens elsewhere.
        _whitelistRepo.WhitelistChanged += OnWhitelistChangedExternally;

        _ = InitializeAsync();
    }

    /// <summary>
    /// Reloads settings and blacklist entries from disk.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            // Unsubscribe first so we don't auto-save while loading.
            if (Settings is not null)
            {
                Settings.PropertyChanged -= OnSettingsPropertyChanged;
            }

            Settings = await _settingsRepo.LoadAsync();
            Settings.PropertyChanged += OnSettingsPropertyChanged;

            // Ensure toggle icon reflects the loaded theme (covers cold-start with Light saved).
            OnPropertyChanged(nameof(IsLightTheme));

            // Reflect the active language in the dropdown (persists the OS-detected default).
            SelectedLanguageCode = !string.IsNullOrWhiteSpace(Settings.Language)
                ? Settings.Language
                : (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    .Equals("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en");

            await ReloadBlacklistEntriesAsync();
            await ReloadWhitelistEntriesAsync();
            StatusMessage = L.T("SettingsLoaded");
            _logger.Info("Settings loaded.");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("SettingsLoadFailed");
            _logger.Error("Settings page failed to load data.", ex);
        }
    }

    /// <summary>
    /// Saves the current application settings to disk.
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            await _settingsRepo.SaveAsync(Settings);
            StatusMessage = L.T("SettingsSaved");
            _logger.Info("Settings saved by user.");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("SettingsSaveFailed");
            _logger.Error("Settings save failed.", ex);
        }
    }

    /// <summary>
    /// Adds a new blacklist entry using the package ID and optional reason entered by the user.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddBlacklistEntry))]
    public async Task AddBlacklistEntryAsync()
    {
        var packageId = NewBlacklistPackageId.Trim();
        if (string.IsNullOrWhiteSpace(packageId))
        {
            StatusMessage = L.T("PackageIdRequired");
            return;
        }

        if (BlacklistEntries.Any(entry => string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = L.T("PackageIdAlreadyBlacklisted");
            return;
        }

        try
        {
            await _blacklistRepo.AddAsync(packageId, NewBlacklistReason);
            await ReloadBlacklistEntriesAsync();

            NewBlacklistPackageId = string.Empty;
            NewBlacklistReason = string.Empty;
            StatusMessage = string.Format(L.T("AddedSingleToBlacklist"), packageId);
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("BlacklistAddFailed");
            _logger.Error("Blacklist add failed.", ex);
        }
    }

    /// <summary>
    /// Removes the currently selected blacklist entry.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedBlacklistEntry))]
    public async Task RemoveSelectedBlacklistEntryAsync()
    {
        if (SelectedBlacklistEntry is null)
        {
            return;
        }

        var packageId = SelectedBlacklistEntry.PackageId;

        try
        {
            await _blacklistRepo.RemoveAsync(packageId);
            await ReloadBlacklistEntriesAsync();
            SelectedBlacklistEntry = null;
            StatusMessage = string.Format(L.T("RemovedSingleFromBlacklist"), packageId);
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("BlacklistRemoveFailed");
            _logger.Error("Blacklist remove failed.", ex);
        }
    }

    /// <summary>
    /// Removes every entry in <paramref name="entries"/> from the repository and
    /// immediately refreshes the visible blacklist. Handles both single- and multi-selection.
    /// </summary>
    /// <param name="entries">The entries to remove. Duplicates are silently ignored.</param>
    public async Task RemoveEntriesAsync(IEnumerable<BlacklistEntry> entries)
    {
        var toRemove = entries.ToList();
        if (toRemove.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var entry in toRemove)
            {
                await _blacklistRepo.RemoveAsync(entry.PackageId);
            }

            // Reload once here for an immediate, definitive UI update.
            // The event-based reloads triggered by each RemoveAsync are benign extras.
            await ReloadBlacklistEntriesAsync();

            StatusMessage = toRemove.Count == 1
                ? string.Format(L.T("RemovedSingleFromBlacklist"), toRemove[0].PackageId)
                : string.Format(L.T("RemovedMultipleFromBlacklist"), toRemove.Count);

            _logger.Info($"Blacklist: removed {toRemove.Count} entry(ies).");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("BlacklistBulkRemoveFailed");
            _logger.Error("Blacklist bulk remove failed.", ex);
        }
    }

    /// <summary>
    /// Removes the currently selected whitelist entry.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedWhitelistEntry))]
    public async Task RemoveSelectedWhitelistEntryAsync()
    {
        if (SelectedWhitelistEntry is null)
        {
            return;
        }

        var entry = SelectedWhitelistEntry;

        try
        {
            await _whitelistRepo.RemoveAsync(entry.Source, entry.Id);
            await ReloadWhitelistEntriesAsync();
            SelectedWhitelistEntry = null;
            StatusMessage = string.Format(L.T("RemovedSingleFromWhitelist"), entry.DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("WhitelistRemoveFailed");
            _logger.Error("Whitelist remove failed.", ex);
        }
    }

    /// <summary>
    /// Removes every entry in <paramref name="entries"/> from the repository and
    /// immediately refreshes the visible whitelist. Handles both single- and multi-selection.
    /// </summary>
    public async Task RemoveWhitelistEntriesAsync(IEnumerable<WhitelistEntry> entries)
    {
        var toRemove = entries.ToList();
        if (toRemove.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var entry in toRemove)
            {
                await _whitelistRepo.RemoveAsync(entry.Source, entry.Id);
            }

            await ReloadWhitelistEntriesAsync();

            StatusMessage = toRemove.Count == 1
                ? string.Format(L.T("RemovedSingleFromWhitelist"), toRemove[0].DisplayName)
                : string.Format(L.T("RemovedMultipleFromWhitelist"), toRemove.Count);

            _logger.Info($"Whitelist: removed {toRemove.Count} entry(ies).");
        }
        catch (Exception ex)
        {
            StatusMessage = L.T("WhitelistBulkRemoveFailed");
            _logger.Error("Whitelist bulk remove failed.", ex);
        }
    }

    private bool CanRemoveSelectedWhitelistEntry() => SelectedWhitelistEntry is not null;

    /// <summary>
    /// Called by the repository whenever the blacklist file changes from any source
    /// (e.g. Programs page context menu). Runs a UI-thread reload so this page stays
    /// in sync even when it is already open.
    /// </summary>
    private void OnBlacklistChangedExternally()
    {
        // The event may fire from a background thread; InvokeAsync queues the work
        // on the WPF dispatcher so ObservableCollection mutations stay on the UI thread.
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await ReloadBlacklistEntriesAsync();
        });
    }

    /// <summary>
    /// Called by the repository whenever the whitelist file changes from any source
    /// (e.g. a page's "Add to Auto-Update" context menu action). Runs a UI-thread reload
    /// so this page stays in sync even when it is already open.
    /// </summary>
    private void OnWhitelistChangedExternally()
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await ReloadWhitelistEntriesAsync();
        });
    }

    private bool CanAddBlacklistEntry()
    {
        return !string.IsNullOrWhiteSpace(NewBlacklistPackageId);
    }

    private bool CanRemoveSelectedBlacklistEntry()
    {
        return SelectedBlacklistEntry is not null;
    }

    /// <summary>
    /// Automatically saves settings when a toggle or dropdown value changes,
    /// so the user does not have to click "Save Settings" for simple changes.
    /// Theme changes also ask <see cref="IThemeService"/> to repaint the app immediately.
    /// </summary>
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ScanOnStartup):
            case nameof(AppSettings.MinimizeToTray):
                _ = SaveAsync();
                break;

            case nameof(AppSettings.RunOnStartup):
                ApplyRunOnStartup(Settings.RunOnStartup);
                _ = SaveAsync();
                break;

            case nameof(AppSettings.Theme):
                OnPropertyChanged(nameof(IsLightTheme));
                ApplyThemeAndSave(Settings.Theme);
                break;
        }
    }

    /// <summary>
    /// Writes or removes the XenUpdate entry in the Windows Run registry key
    /// so the app auto-launches (hidden) at system startup.
    /// </summary>
    private void ApplyRunOnStartup(bool enable)
    {
        const string keyName = "XenUpdate";
        const string runKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (key is null)
            {
                _logger.Warning("Could not open Windows Run registry key.");
                return;
            }

            if (enable)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(keyName, $"\"{exePath}\" -background");
                    _logger.Info("RunOnStartup enabled.");
                }
            }
            else
            {
                key.DeleteValue(keyName, throwOnMissingValue: false);
                _logger.Info("RunOnStartup disabled.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to update RunOnStartup registry key.", ex);
        }
    }

    /// <summary>
    /// Toggles the app theme between <see cref="AppTheme.Dark"/> and <see cref="AppTheme.Light"/>,
    /// applies it immediately, and saves the choice so it persists across restarts.
    /// Bound to the sun/moon toggle button in SettingsView.
    /// </summary>
    [RelayCommand]
    public void ToggleTheme()
    {
        Settings.Theme = Settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        // OnSettingsPropertyChanged listens to Settings.Theme and calls ApplyThemeAndSave.
    }

    /// <summary>
    /// Applies the selected theme to the running app and persists the choice.
    /// Runs sequentially so the visual flip and file write stay in order.
    /// </summary>
    private void ApplyThemeAndSave(AppTheme theme)
    {
        try
        {
            _themeService.ApplyTheme(theme);
            _logger.Info($"Theme switched to {theme}.");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to apply theme.", ex);
        }

        _ = SaveAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadAsync();
    }

    private async Task ReloadBlacklistEntriesAsync()
    {
        var entries = await _blacklistRepo.GetEntriesAsync();

        BlacklistEntries.Clear();
        foreach (var entry in entries)
        {
            BlacklistEntries.Add(entry);
        }

        _logger.Info($"Blacklist loaded: {BlacklistEntries.Count} entry(ies).");
    }

    private async Task ReloadWhitelistEntriesAsync()
    {
        var entries = await _whitelistRepo.GetEntriesAsync();

        WhitelistEntries.Clear();
        foreach (var entry in entries)
        {
            WhitelistEntries.Add(entry);
        }

        _logger.Info($"Whitelist loaded: {WhitelistEntries.Count} entry(ies).");
    }
}
