using System.Text.Json;
using System.Text.Json.Serialization;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Pip;

/// <summary>
/// Parses the JSON output of <c>pip list --outdated --format=json</c> into
/// <see cref="PipPackageItem"/> objects. Pure (string in, list out) so it is fully
/// unit-testable without spawning a process. Unlike winget's plain-text table, pip
/// supports a real JSON output mode, so there is no positional/column-based parsing
/// to keep in sync with locale-dependent header text.
/// </summary>
public sealed class PipListOutputParser
{
    /// <summary>Parses raw "pip list --outdated --format=json" stdout into update items.</summary>
    public IReadOnlyList<PipPackageItem> Parse(string pipOutput)
    {
        if (string.IsNullOrWhiteSpace(pipOutput))
            return Array.Empty<PipPackageItem>();

        List<PipListEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<PipListEntry>>(pipOutput);
        }
        catch (JsonException)
        {
            // pip printed something that isn't the JSON we asked for (a warning banner on
            // stdout, a broken pip installation, etc.) — treat as "nothing parseable" rather
            // than throwing, matching WingetOutputParser's tolerance for unreadable output.
            return Array.Empty<PipPackageItem>();
        }

        if (entries is null)
            return Array.Empty<PipPackageItem>();

        var results = new List<PipPackageItem>(entries.Count);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.LatestVersion))
                continue;

            results.Add(new PipPackageItem
            {
                Id = entry.Name,
                DisplayName = entry.Name,
                PackageName = entry.Name,
                CurrentVersion = string.IsNullOrWhiteSpace(entry.Version) ? "Unknown" : entry.Version,
                AvailableVersion = entry.LatestVersion
            });
        }

        return results;
    }

    // Mirrors pip's --format=json schema for `pip list --outdated`:
    // [{"name": "requests", "version": "2.31.0", "latest_version": "2.32.0", "latest_filetype": "wheel"}]
    private sealed record PipListEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("latest_version")] string LatestVersion);
}
