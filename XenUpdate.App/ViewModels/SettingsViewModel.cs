using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
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

    /// <summary>
    /// A curated Primary/Secondary/Background combo offered as a one-click preset on the
    /// Settings page — background is a full palette, not just a single accent, so picking one
    /// changes the app's whole look (sidebar, surfaces, glass panels, the log/update-queue
    /// windows) consistently, not just button colors.
    /// </summary>
    public sealed record ThemePalette(string Name, string PrimaryHex, string? SecondaryHex, string BackgroundHex)
    {
        /// <summary>Pre-converted brushes for the preset's swatch preview — XAML data binding doesn't run TypeConverters on bound string values the way a literal attribute would.</summary>
        public Brush PrimarySwatch { get; } = (Brush)new BrushConverter().ConvertFromString(PrimaryHex)!;
        public Brush SecondarySwatch { get; } = (Brush)new BrushConverter().ConvertFromString(SecondaryHex ?? PrimaryHex)!;
        public Brush BackgroundSwatch { get; } = (Brush)new BrushConverter().ConvertFromString(BackgroundHex)!;
    }

    /// <summary>
    /// Five curated palettes, replacing an earlier hand-picked set the user found looked bad in
    /// practice (in particular "Blossom", a near-white background that read as broken rather than
    /// intentional against the app's dark-theme text). These are grounded in 2026 SaaS/dashboard
    /// design-trend research instead: dark surfaces settle on well-regarded near-blacks
    /// (#0D1117 GitHub dark, #0F172A Tailwind slate-900, #121212 Material dark) rather than pure
    /// black, and violet/cyan plus navy/purple are called out repeatedly as premium-reading 2026
    /// accent pairings — all four dark entries keep the logo's violet (#7C3AED/#8B5CF6) as
    /// primary. The two light entries use Tailwind's slate-50 and a warm off-white, both paired
    /// with violet so a user who prefers Light mode still gets brand-consistent accents instead of
    /// a generic default blue.
    /// </summary>
    public IReadOnlyList<ThemePalette> ThemePalettes { get; } = new[]
    {
        new ThemePalette("Nova", "#8B5CF6", "#06B6D4", "#0D1117"),
        new ThemePalette("Origin", "#7C3AED", null, "#0F172A"),
        new ThemePalette("Emerald", "#10B981", "#7C3AED", "#121212"),
        new ThemePalette("Daylight", "#7C3AED", "#2563EB", "#F8FAFC"),
        new ThemePalette("Linen", "#7C3AED", "#D97706", "#FAF7F2"),
    };

    /// <summary>
    /// Current Primary/Secondary/Background as brushes for the swatch buttons that open the
    /// inline picker panel (Controls/ColorPickerControl — a self-drawn SV-square + hue strip +
    /// hex box, not the native OS color dialog, which read as visibly out of place against the
    /// rest of this glassmorphism UI). Re-notified from ApplyPaletteAndSave whenever the
    /// underlying hex changes.
    /// </summary>
    public Brush PrimarySwatchBrush => ToBrush(Settings.AccentColorHex);
    public Brush SecondarySwatchBrush => ToBrush(Settings.SecondaryColorHex ?? Settings.AccentColorHex);
    public Brush BackgroundSwatchBrush => ToBrush(Settings.BackgroundColorHex);

    /// <summary>True once a secondary color is explicitly set — drives whether the "revert to automatic" action shows.</summary>
    public bool HasCustomSecondary => !string.IsNullOrWhiteSpace(Settings.SecondaryColorHex);

    /// <summary>
    /// UI mirror of <see cref="AppSettings.SavedCustomColors"/> — reusable single-color
    /// swatches shown inside the color picker panel. Mutated only through the Save/Apply/
    /// Remove commands below, which keep the settings list in lockstep and persist.
    /// </summary>
    public ObservableCollection<string> SavedColors { get; } = new();

    /// <summary>
    /// UI mirror of <see cref="AppSettings.SavedCustomPalettes"/>, projected into the same
    /// <see cref="ThemePalette"/> shape the built-in presets use so both rows share one tile
    /// template and one apply path (SelectThemePaletteCommand). Index-aligned with the
    /// settings list — RemoveSavedPalette relies on that.
    /// </summary>
    public ObservableCollection<ThemePalette> SavedPalettes { get; } = new();

    private static Brush ToBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

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
    /// The live log console, embedded as a card at the bottom of the Settings page — the
    /// separate log window can be closed (or never opened) and the entries stay reachable here.
    /// Same DI singleton instance the LogViewerWindow shows, so the two views never diverge.
    /// </summary>
    public LogConsoleViewModel LogConsole { get; }

    /// <summary>
    /// Initializes the SettingsViewModel with its required repositories.
    /// </summary>
    public SettingsViewModel(
        ISettingsRepository settingsRepo,
        IBlacklistRepository blacklistRepo,
        IWhitelistRepository whitelistRepo,
        ILoggerService logger,
        IThemeService themeService,
        LogConsoleViewModel logConsole)
    {
        _settingsRepo = settingsRepo;
        _blacklistRepo = blacklistRepo;
        _whitelistRepo = whitelistRepo;
        _logger = logger;
        _themeService = themeService;
        LogConsole = logConsole;

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
            SyncSavedAppearance();

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

            // AccentColorHex/SecondaryColorHex/BackgroundColorHex are intentionally NOT handled
            // here — SelectThemePalette/PickPrimaryColor/PickSecondaryColor/ClearSecondaryColor/
            // PickBackgroundColor set the relevant hex(es) and call ApplyPaletteAndSave()
            // explicitly once, rather than reacting to each property change separately (which
            // would apply the palette redundantly for one logical change — harmless, since
            // ApplyPalette is idempotent, but wasteful).

            case nameof(AppSettings.BackgroundImagePath):
            case nameof(AppSettings.BackgroundBlurRadius):
            case nameof(AppSettings.SpotlightEnabled):
                // No service call needed here — MainWindow's background layer binds directly
                // to these AppSettings properties and updates live on its own.
                _ = SaveAsync();
                break;
        }
    }

    /// <summary>Applies the current Primary/Secondary/Background settings to the running app and persists them, mirroring ApplyThemeAndSave.</summary>
    private void ApplyPaletteAndSave()
    {
        try
        {
            if (TryParseHexColor(Settings.AccentColorHex, out var primary)
                && TryParseHexColor(Settings.BackgroundColorHex, out var background))
            {
                // A light background needs the Light theme's dark text (and vice versa) — Zen*
                // text brushes stay whatever the CURRENT theme dictates regardless of how light
                // or dark the chosen background actually is, so without this, a light background
                // picked while still in Dark theme renders light-on-light and is unreadable
                // (exactly what happened before this existed). SetField on AppSettings already
                // no-ops if the theme is already correct, so this is safe to call unconditionally.
                Settings.Theme = Luminance(background) > 0.6 ? AppTheme.Light : AppTheme.Dark;

                Color? secondary = null;
                if (!string.IsNullOrWhiteSpace(Settings.SecondaryColorHex) && TryParseHexColor(Settings.SecondaryColorHex, out var parsedSecondary))
                {
                    secondary = parsedSecondary;
                }

                _themeService.ApplyPalette(primary, secondary, background);
                _logger.Info($"Palette changed: primary={Settings.AccentColorHex}, secondary={Settings.SecondaryColorHex ?? "(auto)"}, background={Settings.BackgroundColorHex}.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to apply palette.", ex);
        }

        // The swatch buttons in Settings read these as computed properties (not bound directly
        // to Settings.XHex), so nothing else would tell WPF to re-read them after a pick/preset.
        OnPropertyChanged(nameof(PrimarySwatchBrush));
        OnPropertyChanged(nameof(SecondarySwatchBrush));
        OnPropertyChanged(nameof(BackgroundSwatchBrush));
        OnPropertyChanged(nameof(HasCustomSecondary));

        _ = SaveAsync();
    }

    /// <summary>Standard perceptual luminance (0=black, 1=white), used to decide whether a background needs Light or Dark theme text to stay readable against it.</summary>
    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

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

    /// <summary>Applies one of the curated <see cref="ThemePalettes"/> — the preset cards pass the palette itself as the command parameter.</summary>
    [RelayCommand]
    private void SelectThemePalette(ThemePalette palette)
    {
        // Set all three, then apply once — OnSettingsPropertyChanged would otherwise fire (and
        // apply) three separate times for one logical change, harmless but wasteful.
        Settings.AccentColorHex = palette.PrimaryHex;
        Settings.SecondaryColorHex = palette.SecondaryHex;
        Settings.BackgroundColorHex = palette.BackgroundHex;
        ApplyPaletteAndSave();
    }

    /// <summary>Which swatch button opened the inline picker panel — None means the panel is closed.</summary>
    public enum ColorPickerTarget { None, Primary, Secondary, Background }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsColorPickerOpen))]
    [NotifyPropertyChangedFor(nameof(ColorPickerTitle))]
    private ColorPickerTarget _activeColorPickerTarget = ColorPickerTarget.None;

    /// <summary>The color the inline picker is currently showing/editing — only committed to Settings (and applied) on Confirm, discarded on Cancel.</summary>
    [ObservableProperty]
    private Color _editingColor;

    public bool IsColorPickerOpen => ActiveColorPickerTarget != ColorPickerTarget.None;

    public string ColorPickerTitle => ActiveColorPickerTarget switch
    {
        ColorPickerTarget.Primary => L.T("SettingsPrimaryHint"),
        ColorPickerTarget.Secondary => L.T("SettingsSecondaryHint"),
        ColorPickerTarget.Background => L.T("SettingsBackgroundHint"),
        _ => string.Empty,
    };

    [RelayCommand]
    private void OpenPrimaryColorPicker() => OpenColorPicker(ColorPickerTarget.Primary, Settings.AccentColorHex);

    [RelayCommand]
    private void OpenSecondaryColorPicker() => OpenColorPicker(ColorPickerTarget.Secondary, Settings.SecondaryColorHex ?? Settings.AccentColorHex);

    [RelayCommand]
    private void OpenBackgroundColorPicker() => OpenColorPicker(ColorPickerTarget.Background, Settings.BackgroundColorHex);

    private void OpenColorPicker(ColorPickerTarget target, string seedHex)
    {
        if (TryParseHexColor(seedHex, out var color))
        {
            EditingColor = color;
        }

        ActiveColorPickerTarget = target;
    }

    /// <summary>Writes EditingColor into whichever hex property is being edited and applies it immediately, same as clicking a preset.</summary>
    [RelayCommand]
    private void ConfirmColorPicker()
    {
        var hex = $"#{EditingColor.R:X2}{EditingColor.G:X2}{EditingColor.B:X2}";
        switch (ActiveColorPickerTarget)
        {
            case ColorPickerTarget.Primary:
                Settings.AccentColorHex = hex;
                break;
            case ColorPickerTarget.Secondary:
                Settings.SecondaryColorHex = hex;
                break;
            case ColorPickerTarget.Background:
                Settings.BackgroundColorHex = hex;
                break;
        }

        ActiveColorPickerTarget = ColorPickerTarget.None;
        ApplyPaletteAndSave();
    }

    /// <summary>Closes the panel without touching Settings — whatever was dragged in the picker is discarded.</summary>
    [RelayCommand]
    private void CancelColorPicker() => ActiveColorPickerTarget = ColorPickerTarget.None;

    /// <summary>Reverts to "no explicit secondary" — ThemeService then derives one from the primary, exactly as if it had never been set.</summary>
    [RelayCommand]
    private void ClearSecondaryColor()
    {
        Settings.SecondaryColorHex = null;
        ApplyPaletteAndSave();
    }

    /// <summary>Rebuilds the observable mirrors from the freshly loaded settings lists.</summary>
    private void SyncSavedAppearance()
    {
        SavedColors.Clear();
        foreach (var hex in Settings.SavedCustomColors)
        {
            SavedColors.Add(hex);
        }

        SavedPalettes.Clear();
        foreach (var palette in Settings.SavedCustomPalettes)
        {
            SavedPalettes.Add(new ThemePalette(palette.Name, palette.PrimaryHex, palette.SecondaryHex, palette.BackgroundHex));
        }
    }

    /// <summary>Adds the color currently shown in the picker to the saved swatches (no duplicates; unlimited otherwise).</summary>
    [RelayCommand]
    private void SaveEditingColor()
    {
        var hex = $"#{EditingColor.R:X2}{EditingColor.G:X2}{EditingColor.B:X2}";
        if (Settings.SavedCustomColors.Contains(hex, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Settings.SavedCustomColors.Add(hex);
        SavedColors.Add(hex);
        _ = SaveAsync();
    }

    /// <summary>Loads a saved swatch into the open picker — Confirm still decides whether it's actually applied.</summary>
    [RelayCommand]
    private void ApplySavedColor(string hex)
    {
        if (TryParseHexColor(hex, out var color))
        {
            EditingColor = color;
        }
    }

    [RelayCommand]
    private void RemoveSavedColor(string hex)
    {
        Settings.SavedCustomColors.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
        SavedColors.Remove(hex);
        _ = SaveAsync();
    }

    /// <summary>Captures the currently applied Primary/Secondary/Background trio as a new saved theme tile.</summary>
    [RelayCommand]
    private void SaveCurrentPalette()
    {
        var saved = new SavedPalette
        {
            Name = string.Format(L.T("CustomPaletteName"), Settings.SavedCustomPalettes.Count + 1),
            PrimaryHex = Settings.AccentColorHex,
            SecondaryHex = Settings.SecondaryColorHex,
            BackgroundHex = Settings.BackgroundColorHex,
        };

        Settings.SavedCustomPalettes.Add(saved);
        SavedPalettes.Add(new ThemePalette(saved.Name, saved.PrimaryHex, saved.SecondaryHex, saved.BackgroundHex));
        _ = SaveAsync();
    }

    /// <summary>Deletes a saved theme tile — index-based because SavedPalettes mirrors Settings.SavedCustomPalettes 1:1 by position.</summary>
    [RelayCommand]
    private void RemoveSavedPalette(ThemePalette palette)
    {
        var index = SavedPalettes.IndexOf(palette);
        if (index < 0)
        {
            return;
        }

        SavedPalettes.RemoveAt(index);
        Settings.SavedCustomPalettes.RemoveAt(index);
        _ = SaveAsync();
    }

    /// <summary>Lets the user pick an image file to show, blurred, behind the app's content.</summary>
    [RelayCommand]
    private void ChooseBackgroundImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            Settings.BackgroundImagePath = dialog.FileName;
        }
    }

    /// <summary>Removes the custom background photo, falling back to the app's built-in ambient gradient.</summary>
    [RelayCommand]
    private void ClearBackgroundImage()
    {
        Settings.BackgroundImagePath = null;
    }

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            var converted = ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : $"#{hex}");
            if (converted is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Falls through to return false below.
        }

        return false;
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
