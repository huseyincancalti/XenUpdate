using System.Runtime.InteropServices;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.WindowsUpdate;

/// <summary>
/// Scans for and installs hardware driver updates by using the Windows Update Agent API (WUApiLib).
/// COM objects are accessed via dynamic late-binding so no COM reference entry is needed in the project file.
/// </summary>
public sealed class DriverUpdateService : IDriverUpdateService
{
    private const string DriverSearchCriteria = "IsInstalled=0 AND Type='Driver' AND IsHidden=0";
    private const int OperationSucceeded = 2;

    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes a new <see cref="DriverUpdateService"/>.
    /// </summary>
    public DriverUpdateService(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DriverUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Driver update scan started.");
        return await Task.Run(() => RunScanOnWorkerThread(cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DriverUpdateInstallResult> InstallUpdateAsync(
        DriverUpdateItem update,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        _logger.Info($"Driver install started for '{update.DisplayName}' (Id={update.Id}).");
        return await Task.Run(
            () => InstallSingleUpdateOnWorkerThread(update, progress, cancellationToken),
            cancellationToken);
    }

    private IReadOnlyList<DriverUpdateItem> RunScanOnWorkerThread(CancellationToken cancellationToken)
    {
        object? session = null;
        object? searcher = null;
        object? searchResult = null;

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                ?? throw new InvalidOperationException(
                    "Windows Update Agent is not available on this machine (ProgID 'Microsoft.Update.Session' not found).");

            session = Activator.CreateInstance(sessionType)!;
            dynamic dynSession = session;

            searcher = dynSession.CreateUpdateSearcher();
            dynamic dynSearcher = searcher;
            TrySetOnline(dynSearcher);

            cancellationToken.ThrowIfCancellationRequested();

            _logger.Info($"Querying Windows Update Agent: {DriverSearchCriteria}");

            searchResult = dynSearcher.Search(DriverSearchCriteria);
            dynamic dynResult = searchResult;

            cancellationToken.ThrowIfCancellationRequested();

            TryLogSearchWarnings(dynResult);

            var items = MapUpdatesToItems(dynResult.Updates);

            if (items.Count == 0)
            {
                _logger.Info("Driver update scan completed. No driver updates were found.");
            }
            else
            {
                _logger.Info($"Driver update scan completed. {items.Count} driver update(s) found.");
            }

            return items;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Driver update scan was cancelled by the user.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Driver update scan encountered an unexpected error.", ex);
            throw;
        }
        finally
        {
            TryReleaseCom(searchResult);
            TryReleaseCom(searcher);
            TryReleaseCom(session);
        }
    }

    private DriverUpdateInstallResult InstallSingleUpdateOnWorkerThread(
        DriverUpdateItem requestedUpdate,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        object? session = null;
        object? searcher = null;
        object? searchResult = null;
        object? targetUpdate = null;
        object? updateCollection = null;
        object? downloader = null;
        object? downloadResult = null;
        object? installer = null;
        object? installResult = null;

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                ?? throw new InvalidOperationException(
                    "Windows Update Agent is not available on this machine (ProgID 'Microsoft.Update.Session' not found).");

            session = Activator.CreateInstance(sessionType)!;
            dynamic dynSession = session;

            searcher = dynSession.CreateUpdateSearcher();
            dynamic dynSearcher = searcher;
            TrySetOnline(dynSearcher);

            cancellationToken.ThrowIfCancellationRequested();

            searchResult = dynSearcher.Search(DriverSearchCriteria);
            dynamic dynResult = searchResult;

            cancellationToken.ThrowIfCancellationRequested();

            targetUpdate = FindMatchingUpdate(dynResult.Updates, requestedUpdate);
            if (targetUpdate is null)
            {
                var detail = "The driver update was no longer available when installation started.";
                _logger.Warning($"Driver install failed for '{requestedUpdate.DisplayName}' (Id={requestedUpdate.Id}). {detail}");
                return new DriverUpdateInstallResult
                {
                    Succeeded = false,
                    DetailMessage = detail
                };
            }

            dynamic dynTargetUpdate = targetUpdate;
            TryAcceptEula(dynTargetUpdate);

            var updateCollectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")
                ?? throw new InvalidOperationException(
                    "Windows Update Agent collection type is not available on this machine.");

            updateCollection = Activator.CreateInstance(updateCollectionType)!;
            dynamic dynUpdateCollection = updateCollection;
            dynUpdateCollection.Add(dynTargetUpdate);

            DownloadUpdateIfNeeded(
                dynSession,
                dynTargetUpdate,
                dynUpdateCollection,
                progress,
                cancellationToken,
                ref downloader,
                ref downloadResult);

            cancellationToken.ThrowIfCancellationRequested();

            installer = dynSession.CreateUpdateInstaller();
            dynamic dynInstaller = installer;
            dynInstaller.Updates = dynUpdateCollection;
            TrySetForceQuiet(dynInstaller);

            progress.Report(75);

            installResult = dynInstaller.Install();
            dynamic dynInstallResult = installResult;

            var resultCode = SafeConvertToInt(dynInstallResult.ResultCode);
            var rebootRequired = SafeConvertToBool(dynInstallResult.RebootRequired);
            var succeeded = resultCode == OperationSucceeded;

            progress.Report(100);

            if (succeeded)
            {
                _logger.Info($"Driver install succeeded for '{requestedUpdate.DisplayName}' (Id={requestedUpdate.Id}). ResultCode={resultCode}.");
            }
            else
            {
                _logger.Warning($"Driver install failed for '{requestedUpdate.DisplayName}' (Id={requestedUpdate.Id}). ResultCode={resultCode}.");
            }

            if (rebootRequired)
            {
                _logger.Info($"Driver install for '{requestedUpdate.DisplayName}' (Id={requestedUpdate.Id}) reported that a restart may be required.");
            }

            return new DriverUpdateInstallResult
            {
                Succeeded = succeeded,
                RebootRequired = rebootRequired,
                ResultCode = resultCode,
                DetailMessage = rebootRequired ? "A restart may be required." : string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Driver install was cancelled for '{requestedUpdate.DisplayName}' (Id={requestedUpdate.Id}).");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Driver install encountered an unexpected error for '{requestedUpdate.DisplayName}' (Id={requestedUpdate.Id}).",
                ex);

            return new DriverUpdateInstallResult
            {
                Succeeded = false,
                DetailMessage = ex.Message
            };
        }
        finally
        {
            TryReleaseCom(installResult);
            TryReleaseCom(installer);
            TryReleaseCom(downloadResult);
            TryReleaseCom(downloader);
            TryReleaseCom(updateCollection);
            TryReleaseCom(targetUpdate);
            TryReleaseCom(searchResult);
            TryReleaseCom(searcher);
            TryReleaseCom(session);
        }
    }

    private static void DownloadUpdateIfNeeded(
        dynamic session,
        dynamic targetUpdate,
        dynamic updateCollection,
        IProgress<int> progress,
        CancellationToken cancellationToken,
        ref object? downloader,
        ref object? downloadResult)
    {
        if (SafeConvertToBool(targetUpdate.IsDownloaded))
        {
            progress.Report(50);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        downloader = session.CreateUpdateDownloader();
        dynamic dynDownloader = downloader;
        dynDownloader.Updates = updateCollection;

        progress.Report(25);
        downloadResult = dynDownloader.Download();
        dynamic dynDownloadResult = downloadResult;

        var resultCode = SafeConvertToInt(dynDownloadResult.ResultCode);
        if (resultCode != OperationSucceeded)
        {
            throw new InvalidOperationException($"Driver update download failed with result code {resultCode}.");
        }

        progress.Report(50);
    }

    private static object? FindMatchingUpdate(dynamic updates, DriverUpdateItem requestedUpdate)
    {
        var count = SafeConvertToInt(updates.Count);

        for (var index = 0; index < count; index++)
        {
            object? candidate = null;

            try
            {
                candidate = updates[index];
                if (candidate is null)
                {
                    continue;
                }

                dynamic dynCandidate = candidate;

                var candidateId = GetUpdateId(dynCandidate);
                var candidateTitle = FirstNonEmpty(GetDriverModel(dynCandidate), SafeConvertToString(dynCandidate.Title));
                var candidateManufacturer = FirstNonEmpty(GetDriverManufacturer(dynCandidate), GetDriverProvider(dynCandidate));
                var candidateClass = GetDriverClass(dynCandidate);

                var idMatches = !string.IsNullOrWhiteSpace(requestedUpdate.Id)
                    && string.Equals(candidateId, requestedUpdate.Id, StringComparison.OrdinalIgnoreCase);

                var titleMatches = string.Equals(candidateTitle, requestedUpdate.DisplayName, StringComparison.OrdinalIgnoreCase);
                var manufacturerMatches = string.IsNullOrWhiteSpace(requestedUpdate.Manufacturer)
                    || string.Equals(candidateManufacturer, requestedUpdate.Manufacturer, StringComparison.OrdinalIgnoreCase);
                var classMatches = string.IsNullOrWhiteSpace(requestedUpdate.DeviceClass)
                    || string.Equals(candidateClass, requestedUpdate.DeviceClass, StringComparison.OrdinalIgnoreCase);

                if (idMatches || (titleMatches && manufacturerMatches && classMatches))
                {
                    return candidate;
                }
            }
            catch
            {
                TryReleaseCom(candidate);
                throw;
            }

            TryReleaseCom(candidate);
        }

        return null;
    }

    private List<DriverUpdateItem> MapUpdatesToItems(dynamic updates)
    {
        var items = new List<DriverUpdateItem>();
        var count = SafeConvertToInt(updates.Count);

        for (var index = 0; index < count; index++)
        {
            try
            {
                dynamic update = updates[index];

                var title = SafeConvertToString(update.Title);
                var driverModel = GetDriverModel(update);
                var manufacturer = GetDriverManufacturer(update);
                var driverProvider = GetDriverProvider(update);
                var deviceClass = GetDriverClass(update);

                var displayName = FirstNonEmpty(driverModel, title, "Driver update");

                items.Add(new DriverUpdateItem
                {
                    Id = FirstNonEmpty(GetUpdateId(update), displayName, $"Driver-{index + 1}"),
                    DisplayName = displayName,
                    Manufacturer = FirstNonEmpty(manufacturer, driverProvider, "Unknown"),
                    DeviceClass = FirstNonEmpty(deviceClass, "Other")
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"A driver update entry could not be mapped and was skipped. {ex.Message}");
            }
        }

        return items;
    }

    private void TryLogSearchWarnings(dynamic searchResult)
    {
        object? warnings = null;

        try
        {
            warnings = searchResult.Warnings;
            dynamic dynWarnings = warnings;

            var count = SafeConvertToInt(dynWarnings.Count);
            if (count > 0)
            {
                _logger.Warning($"Driver update scan completed with {count} warning(s) from Windows Update Agent.");
            }
        }
        catch
        {
            // Warning details are optional. Ignore any issues reading them.
        }
        finally
        {
            TryReleaseCom(warnings);
        }
    }

    private static string GetUpdateId(dynamic update)
    {
        object? identity = null;

        try
        {
            identity = update.Identity;
            if (identity is null)
            {
                return string.Empty;
            }

            dynamic dynIdentity = identity;
            return SafeConvertToString(dynIdentity.UpdateID);
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            TryReleaseCom(identity);
        }
    }

    private static string GetDriverModel(dynamic update)
    {
        var driverModel = TryGetDriverEntryProperty(update, "DriverModel");
        return !string.IsNullOrWhiteSpace(driverModel)
            ? driverModel
            : TryGetDriverProperty(update, "DriverModel");
    }

    private static string GetDriverManufacturer(dynamic update)
    {
        var manufacturer = TryGetDriverEntryProperty(update, "DriverManufacturer");
        return !string.IsNullOrWhiteSpace(manufacturer)
            ? manufacturer
            : TryGetDriverProperty(update, "DriverManufacturer");
    }

    private static string GetDriverClass(dynamic update)
    {
        var driverClass = TryGetDriverEntryProperty(update, "DriverClass");
        return !string.IsNullOrWhiteSpace(driverClass)
            ? driverClass
            : TryGetDriverProperty(update, "DriverClass");
    }

    private static string GetDriverProvider(dynamic update)
    {
        var driverProvider = TryGetDriverEntryProperty(update, "DriverProvider");
        return !string.IsNullOrWhiteSpace(driverProvider)
            ? driverProvider
            : TryGetDriverProperty(update, "DriverProvider");
    }

    private static string TryGetDriverEntryProperty(dynamic update, string propertyName)
    {
        object? entries = null;
        object? entry = null;

        try
        {
            entries = update.WindowsDriverUpdateEntries;
            if (entries is null)
            {
                return string.Empty;
            }

            dynamic dynEntries = entries;
            if (SafeConvertToInt(dynEntries.Count) == 0)
            {
                return string.Empty;
            }

            entry = dynEntries[0];
            if (entry is null)
            {
                return string.Empty;
            }

            dynamic dynEntry = entry;

            return propertyName switch
            {
                "DriverModel" => SafeConvertToString(dynEntry.DriverModel),
                "DriverManufacturer" => SafeConvertToString(dynEntry.DriverManufacturer),
                "DriverClass" => SafeConvertToString(dynEntry.DriverClass),
                "DriverProvider" => SafeConvertToString(dynEntry.DriverProvider),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            TryReleaseCom(entry);
            TryReleaseCom(entries);
        }
    }

    private static string TryGetDriverProperty(dynamic update, string propertyName)
    {
        try
        {
            return propertyName switch
            {
                "DriverModel" => SafeConvertToString(update.DriverModel),
                "DriverManufacturer" => SafeConvertToString(update.DriverManufacturer),
                "DriverClass" => SafeConvertToString(update.DriverClass),
                "DriverProvider" => SafeConvertToString(update.DriverProvider),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryAcceptEula(dynamic update)
    {
        try
        {
            if (!SafeConvertToBool(update.EulaAccepted))
            {
                update.AcceptEula();
            }
        }
        catch
        {
            // Some updates do not expose EULA state consistently; safe to continue.
        }
    }

    private static void TrySetOnline(dynamic searcher)
    {
        try { searcher.Online = true; }
        catch { }
    }

    private static void TrySetForceQuiet(dynamic installer)
    {
        try { installer.ForceQuiet = true; }
        catch { }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static int SafeConvertToInt(object? value)
    {
        try { return Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static bool SafeConvertToBool(object? value)
    {
        try { return Convert.ToBoolean(value); }
        catch { return false; }
    }

    private static string SafeConvertToString(object? value)
    {
        return value?.ToString() ?? string.Empty;
    }

    private static void TryReleaseCom(object? obj)
    {
        if (obj is null)
        {
            return;
        }

        try { Marshal.FinalReleaseComObject(obj); }
        catch { }
    }
}
