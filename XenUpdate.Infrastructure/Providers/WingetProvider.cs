using Microsoft.Management.Deployment;
using System.Security.Principal;
using WindowsPackageManager.Interop;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Providers;

/// <summary>
/// <see cref="IUpdateProvider"/> implementation that discovers and installs
/// application updates via the Winget COM API
/// (<c>Microsoft.Management.Deployment</c> / <c>WindowsPackageManager.Interop</c>).
/// </summary>
public sealed class WingetProvider : IUpdateProvider
{
    // ── Factory helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the correct factory depending on whether the process is elevated.
    /// Using the wrong factory causes a hard crash (no exception) in WinGet COM.
    /// </summary>
    private static WindowsPackageManagerFactory CreateFactory()
    {
        bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

        return isAdmin
            ? new WindowsPackageManagerElevatedFactory()
            : new WindowsPackageManagerStandardFactory();
    }

    // ── IUpdateProvider ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IEnumerable<CategorizedUpdateItem>> GetUpdatesAsync()
    {
        var results = new List<CategorizedUpdateItem>();

        try
        {
            var factory = CreateFactory();
            var manager = factory.CreatePackageManager();

            // Build a composite catalog that searches installed packages against
            // every available remote source so WinGet can compare versions.
            var compositeOptions = factory.CreateCreateCompositePackageCatalogOptions();
            compositeOptions.CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs;

            foreach (var remoteCatalog in manager.GetPackageCatalogs().ToArray())
                compositeOptions.Catalogs.Add(remoteCatalog);

            var catalogRef  = manager.CreateCompositePackageCatalog(compositeOptions);
            var connectResult = catalogRef.Connect();

            if (connectResult.Status != ConnectResultStatus.Ok)
                return results;   // Degrade gracefully — log upstream if needed.

            // Search for all installed packages (empty Id filter = match all).
            var findOptions   = factory.CreateFindPackagesOptions();
            var idFilter      = factory.CreatePackageMatchFilter();
            idFilter.Field    = PackageMatchField.Id;
            idFilter.Option   = PackageFieldMatchOption.ContainsCaseInsensitive;
            idFilter.Value    = string.Empty;
            findOptions.Filters.Add(idFilter);

            var findResult = await connectResult.PackageCatalog.FindPackagesAsync(findOptions);

            foreach (var match in findResult.Matches.ToArray())
            {
                var pkg = match.CatalogPackage;

                // Only surface packages that have an upgrade available.
                var installedVersion  = pkg.InstalledVersion;
                var availableVersion  = pkg.DefaultInstallVersion;

                if (installedVersion is null || availableVersion is null)
                    continue;

                // Compare version strings; skip if already up-to-date.
                if (string.Equals(
                        installedVersion.Version,
                        availableVersion.Version,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new CategorizedUpdateItem
                {
                    Id               = pkg.Id              ?? string.Empty,
                    Name             = pkg.Name            ?? pkg.Id ?? string.Empty,
                    CurrentVersion   = installedVersion.Version  ?? string.Empty,
                    NewVersion       = availableVersion.Version  ?? string.Empty,
                    // WinGet COM API does not expose download size on CatalogPackage;
                    // leave null so DisplaySize renders "Unknown".
                    DownloadSizeBytes = null,
                    Status           = UpdateStatus.Pending,
                    Category         = UpdateCategory.Apps
                });
            }
        }
        catch (Exception)
        {
            // WinGet COM failures are non-fatal: return whatever was collected.
            // The caller (HardwareHubViewModel / ProgramsViewModel) is
            // responsible for surfacing the error via the log console.
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task InstallUpdateAsync(CategorizedUpdateItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Status = UpdateStatus.Installing;

        try
        {
            var factory = CreateFactory();
            var manager = factory.CreatePackageManager();

            // Locate the specific package by exact Id match.
            var compositeOptions = factory.CreateCreateCompositePackageCatalogOptions();
            compositeOptions.CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs;

            foreach (var remoteCatalog in manager.GetPackageCatalogs().ToArray())
                compositeOptions.Catalogs.Add(remoteCatalog);

            var catalogRef    = manager.CreateCompositePackageCatalog(compositeOptions);
            var connectResult = catalogRef.Connect();

            if (connectResult.Status != ConnectResultStatus.Ok)
            {
                item.Status = UpdateStatus.Failed;
                return;
            }

            var findOptions  = factory.CreateFindPackagesOptions();
            var idFilter     = factory.CreatePackageMatchFilter();
            idFilter.Field   = PackageMatchField.Id;
            idFilter.Option  = PackageFieldMatchOption.Equals;
            idFilter.Value   = item.Id;
            findOptions.Filters.Add(idFilter);

            var findResult = await connectResult.PackageCatalog.FindPackagesAsync(findOptions);
            var match      = findResult.Matches.ToArray().FirstOrDefault();

            if (match is null)
            {
                item.Status = UpdateStatus.Failed;
                return;
            }

            var upgradeOptions = factory.CreateInstallOptions();
            upgradeOptions.PackageInstallMode = PackageInstallMode.Silent;

            var upgradeResult = await manager.UpgradePackageAsync(
                match.CatalogPackage, upgradeOptions);

            item.Status = upgradeResult.Status == InstallResultStatus.Ok
                ? UpdateStatus.Succeeded
                : UpdateStatus.Failed;
        }
        catch (Exception)
        {
            item.Status = UpdateStatus.Failed;
        }
    }
}
