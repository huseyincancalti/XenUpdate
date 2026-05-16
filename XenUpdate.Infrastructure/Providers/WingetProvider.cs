using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Providers;

/// <summary>
/// Adapts the command-line Winget scanner and installer to the unified update provider contract.
/// </summary>
public sealed class WingetProvider : IUpdateProvider
{
    private readonly IWingetScanner _scanner;
    private readonly IWingetInstaller _installer;

    /// <summary>Initializes a new Winget provider.</summary>
    public WingetProvider(IWingetScanner scanner, IWingetInstaller installer)
    {
        _scanner = scanner;
        _installer = installer;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<CategorizedUpdateItem>> GetUpdatesAsync()
    {
        try
        {
            var updates = await _scanner.GetAvailableUpdatesAsync(CancellationToken.None);
            return updates.Select(item => new CategorizedUpdateItem
            {
                Id = string.IsNullOrWhiteSpace(item.WingetPackageId) ? item.Id : item.WingetPackageId,
                Name = item.DisplayName,
                CurrentVersion = item.CurrentVersion,
                NewVersion = item.AvailableVersion,
                DownloadSizeBytes = null,
                Status = item.Status,
                Category = UpdateCategory.Apps
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<bool> InstallUpdateAsync(CategorizedUpdateItem item, IProgress<double> progress)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(progress);

        var appUpdate = new AppUpdateItem
        {
            Id = item.Id,
            DisplayName = item.Name,
            CurrentVersion = item.CurrentVersion,
            AvailableVersion = item.NewVersion,
            WingetPackageId = item.Id,
            Status = item.Status,
            IsSelected = item.IsSelected
        };

        item.Status = UpdateStatus.Downloading;
        progress.Report(10);

        try
        {
            var lastProgress = 10d;
            var progressBridge = new Progress<int>(value =>
            {
                var normalizedValue = Math.Clamp(value, 0, 100);
                var mappedValue = normalizedValue < 100
                    ? 15 + normalizedValue * 0.65
                    : 90;

                if (mappedValue > lastProgress)
                {
                    lastProgress = mappedValue;
                    progress.Report(mappedValue);
                }

                item.Status = normalizedValue < 100
                    ? UpdateStatus.Downloading
                    : UpdateStatus.Installing;
            });

            item.Status = UpdateStatus.Installing;
            progress.Report(Math.Max(lastProgress, 35));

            var succeeded = await _installer.InstallUpdateAsync(
                appUpdate,
                progressBridge,
                CancellationToken.None);

            item.Status = succeeded ? UpdateStatus.Installed : UpdateStatus.Failed;
            progress.Report(succeeded ? 100 : 0);
            return succeeded;
        }
        catch
        {
            item.Status = UpdateStatus.Failed;
            progress.Report(0);
            return false;
        }
    }
}
