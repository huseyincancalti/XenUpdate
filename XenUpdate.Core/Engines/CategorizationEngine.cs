using XenUpdate.Core.Enums;
using XenUpdate.Core.Models;

namespace XenUpdate.Core.Engines;

/// <summary>
/// Pure, stateless engine that assigns an <see cref="UpdateCategory"/> to a
/// <see cref="CategorizedUpdateItem"/> based on its raw metadata.
///
/// Decision rules (in priority order):
///   1. Source == Winget                  → Apps
///   2. Source == Driver                  → Drivers
///   3. Source == WindowsUpdate + title
///      contains firmware/bios/hardware   → HardwareHub
///   4. Everything else                   → System
/// </summary>
public static class CategorizationEngine
{
    private static readonly string[] _hardwareKeywords =
        ["firmware", "bios", "uefi", "embedded controller", "intel me",
         "surface", "hardware", "chipset", "thunderbolt"];

    /// <summary>
    /// Inspects <paramref name="item"/> and returns the correct
    /// <see cref="UpdateCategory"/> for it.
    /// </summary>
    public static UpdateCategory Categorize(CategorizedUpdateItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // The item carries an UpdateSource via its originating provider.
        // We infer source from which concrete model/interface set it up.
        // For the aggregated CategorizedUpdateItem we use a simple heuristic
        // based on the item's Id and Name patterns.

        // Rule 1 – Winget packages always have a reverse-DNS package id
        if (IsWingetId(item.Id))
            return UpdateCategory.Apps;

        // Rule 2 – KB articles that are driver-class
        if (item.Id.StartsWith("KB", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(item.Name, ["driver", "controller", "adapter", "dcu"]))
            return UpdateCategory.Drivers;

        // Rule 3 – Firmware / BIOS / hardware-component updates
        if (ContainsAny(item.Name, _hardwareKeywords))
            return UpdateCategory.HardwareHub;

        // Rule 4 – Remaining KB articles → System
        return UpdateCategory.System;
    }

    /// <summary>
    /// Mutates <paramref name="item"/>'s <see cref="CategorizedUpdateItem.Category"/>
    /// in-place and returns the same item (fluent convenience overload).
    /// </summary>
    public static CategorizedUpdateItem ApplyCategory(CategorizedUpdateItem item)
    {
        item.Category = Categorize(item);
        return item;
    }

    /// <summary>
    /// Categorizes an entire collection in one pass. Returns a new list;
    /// original items are mutated (Category property updated).
    /// </summary>
    public static IReadOnlyList<CategorizedUpdateItem> CategorizeAll(
        IEnumerable<CategorizedUpdateItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(ApplyCategory).ToList();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> for typical Winget reverse-DNS package IDs
    /// such as "Microsoft.VisualStudioCode" or "Spotify.Spotify".
    /// </summary>
    private static bool IsWingetId(string id) =>
        !string.IsNullOrWhiteSpace(id) &&
        id.Contains('.') &&
        !id.StartsWith("KB", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string source, IEnumerable<string> keywords) =>
        keywords.Any(k => source.Contains(k, StringComparison.OrdinalIgnoreCase));
}
