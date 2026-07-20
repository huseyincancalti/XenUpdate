using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using XenUpdate.App.Services;
using XenUpdate.App.Startup;
using XenUpdate.App.ViewModels;
using XenUpdate.App.Views;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App;

/// <summary>
/// Application entry point. Configures the DI container and launches the shell window.
/// This is the only place in the application that knows about concrete service implementations.
/// </summary>
public partial class App : Application
{
    /// <summary>The application-wide DI service provider.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// True when the app was launched with <c>-background</c> or <c>-minimized</c>,
    /// e.g. from the Windows Run registry key on system startup.
    /// </summary>
    public static bool IsBackgroundStartup { get; private set; }

    // Guards against the dispatcher and domain handlers both opening the crash window.
    private static int _crashReporting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        IsBackgroundStartup = e.Args.Contains("-background", StringComparer.OrdinalIgnoreCase)
                           || e.Args.Contains("-minimized", StringComparer.OrdinalIgnoreCase);

        var useMocks = e.Args.Contains("--mock", StringComparer.OrdinalIgnoreCase);

        try
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddXenUpdateServices(useMocks);
            Services = serviceCollection.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] DI container build failed: {ex}");
            ShowFallbackWindow("XenUpdate could not initialize its services.\n\n" + ex.Message);
            return;
        }

        // Cosmetic: pre-apply the saved theme before any window is shown.
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
            if (IsBackgroundStartup)
            {
                // Keep the window hidden; tray icon is still active.
                mainWindow.Show();
                mainWindow.Hide();
            }
            else
            {
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] MainWindow.Show failed: {ex}");
            ShowFallbackWindow("XenUpdate could not display its main window.\n\n" + ex.Message);
            return;
        }

        if (shellVm is not null)
        {
            _ = shellVm.RunStartupTasksAsync();
        }
    }

    /// <summary>
    /// Reads the persisted settings once and applies the saved <see cref="Core.Enums.AppTheme"/>.
    /// Failures fall back to the default dark theme so a corrupt settings file never blocks startup.
    /// </summary>
    private static void ApplySavedTheme()
    {
        AppSettings settings;
        try
        {
            // We are on the WPF dispatcher thread. Calling .GetAwaiter().GetResult()
            // directly on an async file read here deadlocks; Task.Run escapes the
            // dispatcher context so the I/O continuation runs on a thread-pool thread.
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
            ApplyLanguage(settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Language apply failed at startup: {ex}");
        }

        // A light background needs the Light theme's dark text (and vice versa) — this is
        // computed BEFORE ApplyTheme below, rather than trusting settings.Theme as saved, so a
        // settings.json saved before this check existed (Theme=Dark with an already-light
        // BackgroundColorHex, the exact broken combination that was reported: white-on-white,
        // unreadable) self-heals on next launch instead of requiring the user to touch a color
        // setting again to trigger the same logic in SettingsViewModel.ApplyPaletteAndSave.
        var effectiveTheme = settings.Theme;
        try
        {
            var backgroundForLuminance = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.BackgroundColorHex)!;
            var luminance = (0.299 * backgroundForLuminance.R + 0.587 * backgroundForLuminance.G + 0.114 * backgroundForLuminance.B) / 255.0;
            effectiveTheme = luminance > 0.6 ? Core.Enums.AppTheme.Light : Core.Enums.AppTheme.Dark;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Background luminance check failed at startup, using saved theme as-is: {ex}");
        }

        try
        {
            Services.GetRequiredService<IThemeService>().ApplyTheme(effectiveTheme);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Theme apply failed at startup: {ex}");
        }

        // Write the corrected value back to settings.json (fire-and-forget, non-blocking) so
        // SettingsViewModel's own load picks up the same theme this method just applied —
        // otherwise its Light/Dark toggle button would keep showing the stale saved value even
        // though the window it's sitting in is now visibly the other theme.
        if (effectiveTheme != settings.Theme)
        {
            settings.Theme = effectiveTheme;
            _ = Task.Run(() => Services.GetRequiredService<ISettingsRepository>().SaveAsync(settings));
        }

        // Applied synchronously here, before MainWindow is even constructed, for the same
        // reason as the theme above: without this, the window would briefly flash the default
        // built-in palette before SettingsViewModel's own (async) load completes and the saved
        // one catches up.
        try
        {
            var primary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.AccentColorHex)!;
            var background = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.BackgroundColorHex)!;
            System.Windows.Media.Color? secondary = string.IsNullOrWhiteSpace(settings.SecondaryColorHex)
                ? null
                : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.SecondaryColorHex)!;

            Services.GetRequiredService<IThemeService>().ApplyPalette(primary, secondary, background);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Palette apply failed at startup: {ex}");
        }
    }

    /// <summary>Applies the saved UI language, or auto-detects it from the OS on first run.</summary>
    private static void ApplyLanguage(AppSettings settings)
    {
        var lang = !string.IsNullOrWhiteSpace(settings.Language) ? settings.Language : DetectOsLanguage();
        LocalizationManager.Instance.ChangeLanguage(lang);
    }

    private static string DetectOsLanguage()
    {
        var ui = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return ui.Equals("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
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

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        AppLogger.AddLog($"[CRASH] {e.Exception.GetType().Name}: {e.Exception.Message}");
        ShowCrashReporter(e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.AddLog($"[FATAL] {ex.GetType().Name}: {ex.Message}");
            ShowCrashReporter(ex);
        }
    }

    private static void ShowCrashReporter(Exception ex)
    {
        if (Interlocked.Exchange(ref _crashReporting, 1) != 0)
        {
            return;
        }

        try
        {
            var win = new CrashReporterWindow(ex);
            win.ShowDialog();
        }
        catch
        {
            // Last-ditch: if even the crash window fails, just exit.
        }

        Environment.Exit(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Every cleanup step below is individually try/caught: if any single step throws,
        // that must NOT skip the Environment.Exit(0) call at the bottom. An uncaught exception
        // partway through this method would abort the rest of it — including the hard exit —
        // which would silently reproduce the exact "still running in Task Manager" bug this
        // method exists to prevent.

        // H.NotifyIcon's TaskbarIcon owns a native hidden window that receives tray messages.
        // If it's never disposed, that window (and the process) can outlive the WPF shutdown
        // sequence entirely — the app disappears from the taskbar/tray but keeps running,
        // visible only in Task Manager. This runs for every exit path (explicit tray "Exit",
        // or the window naturally closing when "Minimize to tray" is off), since OnExit fires
        // regardless of which one triggered shutdown.
        try
        {
            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.AppTrayIcon.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Tray icon dispose failed during exit: {ex}");
        }

        try
        {
            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] Service container dispose failed during exit: {ex}");
        }

        try
        {
            base.OnExit(e);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XenUpdate] base.OnExit failed: {ex}");
        }

        // Belt-and-suspenders: a graceful shutdown is only as reliable as every single
        // library and service involved being well-behaved about background threads. One
        // confirmed repeat offender was ShellViewModel's NetworkMonitorService, which
        // subscribed to NetworkChange.NetworkAvailabilityChanged — a .NET networking API
        // backed by a dedicated, non-background OS-notification thread. A single live
        // foreground thread anywhere is enough to keep the whole process in Task Manager
        // indefinitely, invisible, even after every window has closed, OnExit has finished,
        // and the DI container has disposed every registered service. That specific leak is
        // now fixed (see ShellViewModel.Dispose), but rather than trust the NEXT library or
        // service to also be well-behaved, force the process to actually end here. This must
        // stay the unconditional last line of every exit path — do not gate it behind any
        // condition, and do not remove it even if the underlying leak looks fixed.
        Environment.Exit(0);
    }
}
