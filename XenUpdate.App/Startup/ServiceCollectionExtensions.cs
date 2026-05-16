using Microsoft.Extensions.DependencyInjection;
using XenUpdate.App.Services;
using XenUpdate.Core.Interfaces;
using XenUpdate.Infrastructure.Hardware;
using XenUpdate.Infrastructure.Logging;
using XenUpdate.Infrastructure.Providers;
using XenUpdate.Infrastructure.Storage;
using XenUpdate.Infrastructure.Services;
using XenUpdate.Infrastructure.System;
using XenUpdate.Infrastructure.Winget;
using XenUpdate.Infrastructure.WindowsUpdate;
using XenUpdate.App.ViewModels;

namespace XenUpdate.App.Startup;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that register all
/// XenUpdate services, repositories, and view models.
/// Called once during application startup in <see cref="App"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services with the DI container.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    public static IServiceCollection AddXenUpdateServices(this IServiceCollection services)
    {
        // --- Singletons ---
        // These are created once and shared for the entire lifetime of the application.
        services.AddSingleton<ILoggerService, FileLoggerService>();
        services.AddSingleton<IBlacklistRepository, BlacklistRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IHardwareScannerService, HardwareScannerService>();
        services.AddSingleton<ISystemRestoreService, WindowsSystemRestoreService>();
        services.AddSingleton<IAppUpdateService, GitHubAppUpdateService>();

        // --- Transient (Infrastructure) ---
        // These are stateless helpers — a new instance is fine for each use.
        services.AddTransient<ProcessRunner>();
        services.AddTransient<WingetOutputParser>();
        services.AddTransient<IWingetScanner, WingetScanner>();
        services.AddTransient<IWingetInstaller, WingetInstaller>();
        services.AddTransient<IWindowsUpdateService, WindowsUpdateService>();
        services.AddTransient<IDriverUpdateService, DriverUpdateService>();

        // IUpdateProvider implementations (used by HardwareHubViewModel)
        services.AddTransient<IUpdateProvider, WingetProvider>();
        services.AddTransient<IUpdateProvider, WindowsUpdateProvider>();

        // --- ViewModels (Singleton) ---
        // Singleton preserves state (loaded data, scroll position, etc.)
        // when the user navigates away from a page and comes back.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ProgramsViewModel>();
        services.AddSingleton<WindowsUpdatesViewModel>();
        services.AddSingleton<DriversViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HardwareHubViewModel>();
        services.AddSingleton<LogConsoleViewModel>();

        return services;
    }
}
