using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

// Dev-only fakes; per-member XML docs would be pure noise here.
#pragma warning disable CS1591

namespace XenUpdate.Infrastructure.Mocks;

/// <summary>
/// Fake providers used when the app is launched with <c>--mock</c>. They return
/// representative sample data and simulated progress so the UI can be exercised on
/// any machine without administrator rights, winget, or Windows Update Agent.
/// </summary>
public sealed class MockWingetScanner : IWingetScanner
{
    public async Task<IReadOnlyList<AppUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(600, cancellationToken);
        return new List<AppUpdateItem>
        {
            new() { Id = "Google.Chrome", DisplayName = "Google Chrome", WingetPackageId = "Google.Chrome", Publisher = "Google LLC", CurrentVersion = "125.0.6422.60", AvailableVersion = "126.0.6478.55" },
            new() { Id = "Microsoft.VisualStudioCode", DisplayName = "Visual Studio Code", WingetPackageId = "Microsoft.VisualStudioCode", Publisher = "Microsoft", CurrentVersion = "1.89.1", AvailableVersion = "1.90.0" },
            new() { Id = "VideoLAN.VLC", DisplayName = "VLC media player", WingetPackageId = "VideoLAN.VLC", Publisher = "VideoLAN", CurrentVersion = "3.0.20", AvailableVersion = "3.0.21" },
            new() { Id = "7zip.7zip", DisplayName = "7-Zip", WingetPackageId = "7zip.7zip", Publisher = "Igor Pavlov", CurrentVersion = "23.01", AvailableVersion = "24.05" }
        };
    }
}

public sealed class MockWingetInstaller : IWingetInstaller
{
    public async Task<bool> InstallUpdateAsync(AppUpdateItem item, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        const double totalMb = 84.0;
        for (var percent = 0; percent <= 100; percent += 20)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var downloadedMb = totalMb * percent / 100.0;
            progress.Report(new InstallProgress(percent, $"{downloadedMb:0.0} MB / {totalMb:0.0} MB"));
            await Task.Delay(160, cancellationToken);
        }
        return true;
    }
}

public sealed class MockWindowsUpdateService : IWindowsUpdateService
{
    public async Task<IReadOnlyList<WindowsUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(800, cancellationToken);
        return new List<WindowsUpdateItem>
        {
            new() { Id = "KB5039212", DisplayName = "2024-06 Cumulative Update for Windows 11 (KB5039212)", KbArticleId = "KB5039212", MsrcSeverity = "Critical", IsImportant = true },
            new() { Id = "KB5039338", DisplayName = ".NET 8.0.6 Security Update (KB5039338)", KbArticleId = "KB5039338", MsrcSeverity = "Important", IsImportant = true },
            new() { Id = "KB890830", DisplayName = "Windows Malicious Software Removal Tool (KB890830)", KbArticleId = "KB890830", MsrcSeverity = "Moderate" }
        };
    }

    public async Task<WindowsUpdateInstallResult> InstallUpdateAsync(WindowsUpdateItem update, IProgress<int> progress, CancellationToken cancellationToken)
    {
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(percent);
            await Task.Delay(220, cancellationToken);
        }
        return new WindowsUpdateInstallResult { Succeeded = true, RebootRequired = update.IsImportant, ResultCode = 2 };
    }
}

public sealed class MockDriverUpdateService : IDriverUpdateService
{
    public async Task<IReadOnlyList<DriverUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(800, cancellationToken);
        return new List<DriverUpdateItem>
        {
            new() { Id = "nv-display", DisplayName = "NVIDIA GeForce RTX 4070", Manufacturer = "NVIDIA Corporation", DeviceClass = "Display" },
            new() { Id = "intel-net", DisplayName = "Intel(R) Wi-Fi 6E AX211 160MHz", Manufacturer = "Intel Corporation", DeviceClass = "Network" }
        };
    }

    public async Task<DriverUpdateInstallResult> InstallUpdateAsync(DriverUpdateItem update, IProgress<int> progress, CancellationToken cancellationToken)
    {
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(percent);
            await Task.Delay(260, cancellationToken);
        }
        return new DriverUpdateInstallResult { Succeeded = true, RebootRequired = true, ResultCode = 2 };
    }
}

public sealed class MockHardwareScannerService : IHardwareScannerService
{
    public Task<HardwareProfile> GetCurrentHardwareAsync() =>
        Task.FromResult(new HardwareProfile
        {
            GpuName = "NVIDIA GeForce RTX 4070",
            GpuVendor = "NVIDIA",
            CpuName = "Intel(R) Core(TM) i7-13700K"
        });
}

public sealed class MockSystemRestoreService : ISystemRestoreService
{
    public Task<bool> CreateRestorePointAsync(string description) => Task.FromResult(true);
}

public sealed class MockAppUpdateService : IAppUpdateService
{
    public Task<(bool HasUpdate, string UpdateUrl)> CheckForAppUpdatesAsync(string currentVersion) =>
        Task.FromResult((false, string.Empty));
}

public sealed class MockNvidiaDriverService : INvidiaDriverService
{
    public Task<DriverUpdateStatus> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DriverUpdateStatus
        {
            Checked = true,
            InstalledVersion = "566.14",
            LatestVersion = "566.36",
            UpdateAvailable = true,
            DownloadUrl = "https://www.nvidia.com/download/index.aspx"
        });
}

public sealed class MockPipScanner : IPipScanner
{
    public async Task<IReadOnlyList<PipPackageItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(600, cancellationToken);
        return new List<PipPackageItem>
        {
            new() { Id = "requests", DisplayName = "requests", PackageName = "requests", CurrentVersion = "2.31.0", AvailableVersion = "2.32.0" },
            new() { Id = "numpy", DisplayName = "numpy", PackageName = "numpy", CurrentVersion = "1.26.0", AvailableVersion = "2.0.0" },
            new() { Id = "flask", DisplayName = "flask", PackageName = "flask", CurrentVersion = "3.0.0", AvailableVersion = "3.1.0" }
        };
    }
}

public sealed class MockPipInstaller : IPipInstaller
{
    public async Task<bool> InstallUpdateAsync(PipPackageItem item, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new InstallProgress(percent));
            await Task.Delay(180, cancellationToken);
        }
        return true;
    }
}
