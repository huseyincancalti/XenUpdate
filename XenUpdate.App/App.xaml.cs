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
            Services.GetRequiredService<IThemeService>().ApplyTheme(settings.Theme);
        }
        catch (Exception ex)
        {
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
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnExit(e);
    }
}
