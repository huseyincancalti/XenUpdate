using Microsoft.Extensions.DependencyInjection;
using XenUpdate.App.Services;
using XenUpdate.Core.Interfaces;
using XenUpdate.Infrastructure.Guides;
using XenUpdate.Infrastructure.Hardware;
using XenUpdate.Infrastructure.Logging;
using XenUpdate.Infrastructure.Mocks;
using XenUpdate.Infrastructure.Storage;
using XenUpdate.Infrastructure.Services;
using XenUpdate.Infrastructure.System;
using XenUpdate.Infrastructure.Winget;
using XenUpdate.Infrastructure.WindowsUpdate;
using XenUpdate.App.ViewModels;

namespace XenUpdate.App.Startup;

/// <summary>
/// Registers all XenUpdate services, repositories, and view models with the DI container.
/// Called once during application startup.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="useMocks">
    /// When true (app launched with <c>--mock</c>), update/hardware/restore providers are
    /// replaced with fakes so the UI can be run without admin rights, winget, or WUA.
    /// </param>
    public static IServiceCollection AddXenUpdateServices(this IServiceCollection services, bool useMocks = false)
    {
        // --- Shared singletons (real in every mode) ---
        services.AddSingleton<ILoggerService, FileLoggerService>();
        services.AddSingleton<IBlacklistRepository, BlacklistRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IGuideCatalog, EmbeddedGuideCatalog>();
        services.AddSingleton<IGuideCompletionStore, JsonGuideCompletionStore>();

        if (useMocks)
        {
            services.AddSingleton<IHardwareScannerService, MockHardwareScannerService>();
            services.AddSingleton<ISystemRestoreService, MockSystemRestoreService>();
            services.AddSingleton<IAppUpdateService, MockAppUpdateService>();
            services.AddTransient<IWingetScanner, MockWingetScanner>();
            services.AddTransient<IWingetInstaller, MockWingetInstaller>();
            services.AddTransient<IWindowsUpdateService, MockWindowsUpdateService>();
            services.AddTransient<IDriverUpdateService, MockDriverUpdateService>();
        }
        else
        {
            services.AddSingleton<IHardwareScannerService, HardwareScannerService>();
            services.AddSingleton<ISystemRestoreService, WindowsSystemRestoreService>();
            services.AddSingleton<IAppUpdateService, GitHubAppUpdateService>();
            services.AddTransient<ProcessRunner>();
            services.AddTransient<WingetOutputParser>();
            services.AddTransient<IWingetScanner, WingetScanner>();
            services.AddTransient<IWingetInstaller, WingetInstaller>();
            services.AddTransient<IWindowsUpdateService, WindowsUpdateService>();
            services.AddTransient<IDriverUpdateService, DriverUpdateService>();
        }

        // --- ViewModels (Singleton: preserve page state across navigation) ---
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
