using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Providers;

/// <summary>
/// <see cref="IUpdateProvider"/> implementation that surfaces Windows OS patches
/// and hardware driver updates by calling the Windows Update Agent COM API via
/// late-bound <c>dynamic</c> interop (no .csproj COM reference required).
///
/// All blocking WUA calls are run on the thread pool via <see cref="Task.Run(Action)"/>
/// so the WPF dispatcher is never blocked.
/// </summary>
public sealed class WindowsUpdateProvider : IUpdateProvider
{
    // ── Category-detection keywords ───────────────────────────────────────────

    private static readonly string[] _driverKeywords =
    [
        "driver", "controller", "adapter", "firmware", "chipset",
        "nvidia", "amd", "intel graphics", "realtek", "atheros",
        "broadcom", "qualcomm", "bluetooth", "wi-fi", "wireless",
        "thunderbolt", "usb hub", "hid ", "dcu", "display driver"
    ];

    // ── IUpdateProvider ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<IEnumerable<CategorizedUpdateItem>> GetUpdatesAsync() =>
        Task.Run<IEnumerable<CategorizedUpdateItem>>(SearchUpdates);

    /// <inheritdoc/>
    public Task InstallUpdateAsync(CategorizedUpdateItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Task.Run(() => PerformInstall(item));
    }

    // ── Private blocking helpers (run on thread pool) ─────────────────────────

    private static List<CategorizedUpdateItem> SearchUpdates()
    {
        var results = new List<CategorizedUpdateItem>();

        try
        {
            // Late-bind to avoid requiring a COM reference in the .csproj.
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                              ?? throw new InvalidOperationException(
                                  "Windows Update Session COM object not found.");

            dynamic session = Activator.CreateInstance(sessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();

            // Search for updates that are not installed and not hidden.
            dynamic searchResult = searcher.Search("IsInstalled=0 and IsHidden=0");
            dynamic updates = searchResult.Updates;

            int count = (int)updates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic update = updates.Item(i);

                // ── Category detection ────────────────────────────────────────
                // WUA Type: 1 = Software, 2 = Driver.
                int wuaType = (int)update.Type;
                UpdateCategory category = wuaType == 2
                    ? UpdateCategory.Drivers
                    : ClassifyByTitle((string)update.Title);

                // ── Download size ─────────────────────────────────────────────
                // MaxDownloadSize is a decimal on some WUA versions; cast safely.
                ulong? downloadSize = null;
                try
                {
                    long raw = (long)update.MaxDownloadSize;
                    if (raw > 0) downloadSize = (ulong)raw;
                }
                catch { /* Property unavailable on some update types. */ }

                // ── KB article ID ─────────────────────────────────────────────
                string kbId = ExtractKbId(update);

                results.Add(new CategorizedUpdateItem
                {
                    Id                = kbId,
                    Name              = (string)update.Title,
                    CurrentVersion    = string.Empty,   // WUA does not expose installed version.
                    NewVersion        = string.Empty,   // WUA does not expose a "new version" string.
                    DownloadSizeBytes = downloadSize,
                    Status            = UpdateStatus.Pending,
                    Category          = category
                });
            }
        }
        catch (Exception)
        {
            // WUA COM failures are non-fatal; return whatever was collected.
        }

        return results;
    }

    private static void PerformInstall(CategorizedUpdateItem item)
    {
        item.Status = UpdateStatus.Downloading;

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                              ?? throw new InvalidOperationException(
                                  "Windows Update Session COM object not found.");

            dynamic session = Activator.CreateInstance(sessionType)!;

            // ── Find the update by matching the KB article ID / title ─────────
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic searchResult = searcher.Search("IsInstalled=0 and IsHidden=0");
            dynamic allUpdates = searchResult.Updates;

            dynamic? target = null;
            int count = (int)allUpdates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic candidate = allUpdates.Item(i);
                string title = (string)candidate.Title;
                string kbId  = ExtractKbId(candidate);

                if (string.Equals(kbId,   item.Id,   StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(title,  item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    target = candidate;
                    break;
                }
            }

            if (target is null)
            {
                item.Status = UpdateStatus.Failed;
                return;
            }

            // ── Build a single-item update collection ─────────────────────────
            var collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")
                                 ?? throw new InvalidOperationException(
                                     "Microsoft.Update.UpdateColl COM object not found.");

            dynamic updateColl = Activator.CreateInstance(collectionType)!;
            updateColl.Add(target);

            // ── Download ──────────────────────────────────────────────────────
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = updateColl;
            dynamic downloadResult = downloader.Download();
            int downloadCode = (int)downloadResult.ResultCode;

            // ResultCode: 2 = orcSucceeded, 3 = orcSucceededWithErrors
            if (downloadCode != 2 && downloadCode != 3)
            {
                item.Status = UpdateStatus.Failed;
                return;
            }

            // ── Install ───────────────────────────────────────────────────────
            item.Status = UpdateStatus.Installing;

            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = updateColl;
            dynamic installResult = installer.Install();
            int installCode = (int)installResult.ResultCode;

            item.Status = (installCode == 2 || installCode == 3)
                ? UpdateStatus.Succeeded
                : UpdateStatus.Failed;
        }
        catch (Exception)
        {
            item.Status = UpdateStatus.Failed;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Infers <see cref="UpdateCategory"/> from the update title when WUA reports
    /// <c>Type == 1</c> (Software) but the title contains driver-related keywords.
    /// </summary>
    private static UpdateCategory ClassifyByTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return UpdateCategory.System;

        foreach (var keyword in _driverKeywords)
            if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return UpdateCategory.Drivers;

        return UpdateCategory.System;
    }

    /// <summary>
    /// Extracts the first KB article ID from the update's KBArticleIDs collection,
    /// falling back to the update's Identity.UpdateID string.
    /// </summary>
    private static string ExtractKbId(dynamic update)
    {
        try
        {
            dynamic kbIds = update.KBArticleIDs;
            if ((int)kbIds.Count > 0)
                return "KB" + (string)kbIds.Item(0);
        }
        catch { /* Ignore; fall through to Identity. */ }

        try { return (string)update.Identity.UpdateID; }
        catch { return string.Empty; }
    }
}
