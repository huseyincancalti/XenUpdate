using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ZenUpdate.App.Services;
using ZenUpdate.App.Startup;
using ZenUpdate.App.ViewModels;
using ZenUpdate.Core.Interfaces;
using ZenUpdate.Core.Models;

namespace ZenUpdate.App;

/// <summary>
/// Application entry point. Configures the DI container and launches <see cref="MainWindow"/>.
/// This is the only place in the application that knows about concrete service implementations.
/// </summary>
public partial class App : Application
{
    /// <summary>The application-wide DI service provider.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Called by WPF when the application starts.
    /// Builds the service container and opens the main window.
    /// Every step is individually guarded so a bad settings file or a broken
    /// theme resource never prevents <see cref="MainWindow"/> from showing.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Migrate settings/blacklist from old %APPDATA%\ZenUpdate to new %APPDATA%\XenUpdate.
        MigrateAppDataFolder();

        try
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddZenUpdateServices();
            Services = serviceCollection.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] DI container build failed: {ex}");
            ShowFallbackWindow("XenUpdate could not initialize its services.\n\n" + ex.Message);
            return;
        }

        // Cosmetic: pre-apply the saved theme before any window is shown.
        // Failures here are swallowed inside ApplySavedTheme so they cannot
        // block the MainWindow.Show() call below.
        ApplySavedTheme();

        MainWindow? mainWindow = null;
        try
        {
            mainWindow = new MainWindow();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] MainWindow construction failed: {ex}");
            ShowFallbackWindow("XenUpdate could not load its main window.\n\n" + ex.Message);
            return;
        }

        ShellViewModel? shellVm = null;
        try
        {
            shellVm = Services.GetRequiredService<ShellViewModel>();
            mainWindow.DataContext = shellVm;
        }
        catch (Exception ex)
        {
            // The window will still appear (empty); the user can at least close it.
            Debug.WriteLine($"[XenUpdate] ShellViewModel resolution failed: {ex}");
        }

        try
        {
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] MainWindow.Show failed: {ex}");
            ShowFallbackWindow("XenUpdate could not display its main window.\n\n" + ex.Message);
            return;
        }

        if (shellVm is not null)
        {
            // Trigger an auto-scan after the window appears if the user enabled it.
            // We fire-and-forget from the UI thread; ScanAsync is already async-safe.
            _ = TriggerStartupScanAsync(shellVm);
        }
    }

    /// <summary>
    /// Reads the persisted settings once and asks <see cref="IThemeService"/> to apply
    /// the saved <see cref="Core.Enums.AppTheme"/>. Failures fall back to the default
    /// dark theme so a corrupt settings file never blocks startup.
    /// </summary>
    private static void ApplySavedTheme()
    {
        AppSettings settings;
        try
        {
            // CRITICAL: We are on the WPF dispatcher thread. Calling
            // `.GetAwaiter().GetResult()` directly on an async file read
            // here deadlocks: the awaited continuation tries to resume on
            // the dispatcher SynchronizationContext that we are blocking.
            // Wrapping the call in Task.Run escapes the dispatcher context
            // so the I/O continuation runs on a thread-pool thread instead.
            // This is the real reason the previous startup hung whenever
            // %APPDATA%\XenUpdate\settings.json existed.
            settings = Task.Run(static () =>
                Services.GetRequiredService<ISettingsRepository>().LoadAsync()
            ).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Settings load failed at startup, using defaults: {ex}");
            settings = new AppSettings();
        }

        try
        {
            Services.GetRequiredService<IThemeService>().ApplyTheme(settings.Theme);
        }
        catch (Exception ex)
        {
            // Theme is purely cosmetic. Startup must continue even if the
            // theme dictionaries cannot be merged for any reason.
            Debug.WriteLine($"[XenUpdate] Theme apply failed at startup: {ex}");
        }
    }

    /// <summary>
    /// Last-ditch recovery window so the app never silently exits when something
    /// catastrophic happens during <see cref="OnStartup"/>.
    /// </summary>
    private static void ShowFallbackWindow(string message)
    {
        try
        {
            new Window
            {
                Title = "XenUpdate (Recovery)",
                Width = 480,
                Height = 220,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = message,
                    Margin = new Thickness(16),
                    TextWrapping = TextWrapping.Wrap
                }
            }.Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Recovery window failed to show: {ex}");
        }
    }

    /// <summary>
    /// Waits for the SettingsViewModel to finish loading from disk,
    /// then triggers a winget scan if <see cref="AppSettings.ScanOnStartup"/> is true.
    /// </summary>
    private static async Task TriggerStartupScanAsync(ShellViewModel shellVm)
    {
        try
        {
            // Give the async InitializeAsync in SettingsViewModel time to complete.
            await Task.Delay(400);

            var settingsVm = Services.GetRequiredService<SettingsViewModel>();
            if (!settingsVm.Settings.ScanOnStartup)
            {
                return;
            }

            var programsVm = Services.GetRequiredService<ProgramsViewModel>();
            if (programsVm.ScanCommand.CanExecute(null))
            {
                // Navigate to Programs so the user sees the scan in progress.
                shellVm.NavigateTo(AppPage.Programs);
                programsVm.ScanCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Startup scan trigger failed: {ex}");
        }
    }

    /// <summary>
    /// Called when the application is shutting down.
    /// Disposes the service provider to clean up any disposable services.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>
    /// One-time migration from the old <c>%APPDATA%\ZenUpdate</c> folder to the new
    /// <c>%APPDATA%\XenUpdate</c> folder. Copies settings.json, blacklist.json, and
    /// the logs subfolder if they exist. Silently skipped if:
    ///   - the old folder does not exist, or
    ///   - the new folder already exists (migration was already done or user started fresh).
    /// Failures are swallowed so they never block startup.
    /// </summary>
    private static void MigrateAppDataFolder()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var oldDir = Path.Combine(appData, "ZenUpdate");
            var newDir = Path.Combine(appData, "XenUpdate");

            if (!Directory.Exists(oldDir) || Directory.Exists(newDir))
            {
                return;
            }

            Directory.CreateDirectory(newDir);

            // Copy top-level files (settings.json, blacklist.json, etc.)
            foreach (var file in Directory.GetFiles(oldDir))
            {
                var destFile = Path.Combine(newDir, Path.GetFileName(file));
                try
                {
                    File.Copy(file, destFile, overwrite: false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[XenUpdate] Migration: could not copy {file}: {ex.Message}");
                }
            }

            // Copy logs subfolder
            var oldLogs = Path.Combine(oldDir, "logs");
            var newLogs = Path.Combine(newDir, "logs");
            if (Directory.Exists(oldLogs))
            {
                Directory.CreateDirectory(newLogs);
                foreach (var logFile in Directory.GetFiles(oldLogs))
                {
                    var destFile = Path.Combine(newLogs, Path.GetFileName(logFile));
                    try
                    {
                        File.Copy(logFile, destFile, overwrite: false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[XenUpdate] Migration: could not copy log {logFile}: {ex.Message}");
                    }
                }
            }

            Debug.WriteLine("[XenUpdate] AppData migration from ZenUpdate to XenUpdate completed.");
        }
        catch (Exception ex)
        {
            // Migration is best-effort. Never crash on migration failure.
            Debug.WriteLine($"[XenUpdate] AppData migration failed (non-fatal): {ex.Message}");
        }
    }
}
