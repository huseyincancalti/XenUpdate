using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using static XenUpdate.Infrastructure.WindowsUpdate.WuaComHelpers;

namespace XenUpdate.Infrastructure.WindowsUpdate;

/// <summary>
/// Scans for available Windows OS software updates using the Windows Update Agent API (WUApiLib).
/// COM objects are accessed via dynamic late-binding so no COM reference entry is needed in the project file.
/// All COM objects are released in <c>finally</c> blocks to prevent background WUA processes from hanging.
/// </summary>
public sealed class WindowsUpdateService : IWindowsUpdateService
{
    private const int OperationSucceeded = 2;

    private readonly ILoggerService _logger;

    /// <summary>Initializes a new <see cref="WindowsUpdateService"/>.</summary>
    public WindowsUpdateService(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The first call can take 30-120 seconds while Windows Update Agent contacts Microsoft servers.
    /// Subsequent calls in the same session are faster.
    /// </remarks>
    public async Task<IReadOnlyList<WindowsUpdateItem>> GetAvailableUpdatesAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Windows Update scan started.");
        return await Task.Run(() => RunScanOnWorkerThread(cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WindowsUpdateInstallResult> InstallUpdateAsync(
        WindowsUpdateItem update,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        _logger.Info($"Windows Update install started for '{update.DisplayName}'.");
        return await Task.Run(() => InstallSingleUpdateOnWorkerThread(update, progress, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<WindowsUpdateItem> RunScanOnWorkerThread(CancellationToken cancellationToken)
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

            _logger.Info("Querying Windows Update Agent: IsInstalled=0 AND Type='Software' AND IsHidden=0");

            searchResult = dynSearcher.Search("IsInstalled=0 AND Type='Software' AND IsHidden=0");
            dynamic dynResult = searchResult;

            cancellationToken.ThrowIfCancellationRequested();

            var items = MapUpdatesToItems(dynResult.Updates);

            if (items.Count == 0)
            {
                _logger.Info("Windows Update scan complete. No pending updates found - the system is up to date.");
            }
            else
            {
                _logger.Info($"Windows Update scan completed. {items.Count} update(s) found.");
            }

            return items;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Windows Update scan was cancelled by the user.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Windows Update scan encountered an unexpected error.", ex);
            throw;
        }
        finally
        {
            TryReleaseCom(searchResult);
            TryReleaseCom(searcher);
            TryReleaseCom(session);
        }
    }

    private WindowsUpdateInstallResult InstallSingleUpdateOnWorkerThread(
        WindowsUpdateItem requestedUpdate,
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

            searchResult = dynSearcher.Search("IsInstalled=0 AND Type='Software' AND IsHidden=0");
            dynamic dynResult = searchResult;

            cancellationToken.ThrowIfCancellationRequested();

            targetUpdate = FindMatchingUpdate(dynResult.Updates, requestedUpdate);
            if (targetUpdate is null)
            {
                var detail = "The update was no longer available when installation started.";
                _logger.Warning($"Windows Update install failed for '{requestedUpdate.DisplayName}'. {detail}");
                return new WindowsUpdateInstallResult
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

            DownloadUpdateIfNeeded(dynSession, dynTargetUpdate, dynUpdateCollection, progress, cancellationToken, ref downloader, ref downloadResult);

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

            var detailMessage = rebootRequired
                ? "A restart may be required."
                : string.Empty;

            if (succeeded)
            {
                _logger.Info($"Windows Update install succeeded for '{requestedUpdate.DisplayName}'. ResultCode={resultCode}.");
            }
            else
            {
                _logger.Warning($"Windows Update install failed for '{requestedUpdate.DisplayName}'. ResultCode={resultCode}.");
            }

            if (rebootRequired)
            {
                _logger.Info($"Windows Update install for '{requestedUpdate.DisplayName}' reported that a restart may be required.");
            }

            return new WindowsUpdateInstallResult
            {
                Succeeded = succeeded,
                RebootRequired = rebootRequired,
                ResultCode = resultCode,
                DetailMessage = detailMessage
            };
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Windows Update install was cancelled for '{requestedUpdate.DisplayName}'.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Windows Update install encountered an unexpected error for '{requestedUpdate.DisplayName}'.", ex);
            return new WindowsUpdateInstallResult
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
            throw new InvalidOperationException($"Windows Update download failed with result code {resultCode}.");
        }

        progress.Report(50);
    }

    private static object? FindMatchingUpdate(dynamic updates, WindowsUpdateItem requestedUpdate)
    {
        var count = SafeConvertToInt(updates.Count);

        for (var i = 0; i < count; i++)
        {
            object? candidate = null;

            try
            {
                candidate = updates[i];
                if (candidate is null)
                {
                    continue;
                }

                dynamic dynCandidate = candidate;

                var candidateTitle = SafeConvertToString(dynCandidate.Title);
                var candidateKbId = GetFirstKbId(dynCandidate);

                var titleMatches = string.Equals(candidateTitle, requestedUpdate.DisplayName, StringComparison.OrdinalIgnoreCase);
                var kbMatches = !string.IsNullOrWhiteSpace(requestedUpdate.KbArticleId)
                    && string.Equals(candidateKbId, requestedUpdate.KbArticleId, StringComparison.OrdinalIgnoreCase);

                if (titleMatches || kbMatches)
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

    private static List<WindowsUpdateItem> MapUpdatesToItems(dynamic updates)
    {
        var items = new List<WindowsUpdateItem>();
        var count = SafeConvertToInt(updates.Count);

        for (var i = 0; i < count; i++)
        {
            dynamic update = updates[i];

            var title = SafeConvertToString(update.Title);
            var kbId = GetFirstKbId(update);
            var severity = TryGetSeverity(update);
            var isImportant = severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)
                           || severity.Equals("Important", StringComparison.OrdinalIgnoreCase);

            items.Add(new WindowsUpdateItem
            {
                Id = string.IsNullOrEmpty(kbId) ? title : kbId,
                DisplayName = title,
                KbArticleId = kbId,
                MsrcSeverity = severity,
                IsImportant = isImportant,
            });
        }

        return items;
    }

    private static string GetFirstKbId(dynamic update)
    {
        try
        {
            dynamic kbIds = update.KBArticleIDs;
            if (SafeConvertToInt(kbIds.Count) == 0)
            {
                return string.Empty;
            }

            var raw = SafeConvertToString(kbIds[0]);
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : "KB" + raw;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryGetSeverity(dynamic update)
    {
        try
        {
            return SafeConvertToString(update.MsrcSeverity);
        }
        catch
        {
            return string.Empty;
        }
    }

}
