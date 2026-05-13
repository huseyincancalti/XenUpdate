using System.Text.RegularExpressions;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Winget;

/// <summary>
/// Parses the raw text output of "winget upgrade" into a list of
/// <see cref="AppUpdateItem"/> objects.
///
/// This class has NO I/O dependencies — it takes a string and returns a list.
/// That makes it 100% unit-testable without spawning any process.
///
/// Strategy: locate the table header line, record the start column of each
/// field (Id, Version, Available, Source), then extract substrings from each
/// data row using those fixed column offsets.
/// </summary>
public sealed class WingetOutputParser
{
    // Matches VT100/ANSI terminal escape sequences such as \x1b[0m or \x1b[31m.
    private static readonly Regex AnsiRegex =
        new(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    // Matches the summary footer line: "3 upgrades available." or "No applicable upgrades found."
    private static readonly Regex FooterRegex =
        new(@"^\d+ upgrade|no applicable|no installed|winget upgrade|^$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses the complete stdout text from "winget upgrade" and returns
    /// all update items that could be read successfully.
    /// Malformed or unreadable rows are silently skipped.
    /// </summary>
    /// <param name="wingetOutput">Raw string captured from the winget process stdout.</param>
    /// <returns>
    /// A read-only list of parsed update items.
    /// Returns an empty list when no updates are found or the output is unrecognized.
    /// </returns>
    public IReadOnlyList<AppUpdateItem> Parse(string wingetOutput)
    {
        if (string.IsNullOrWhiteSpace(wingetOutput))
            return Array.Empty<AppUpdateItem>();

        // 1 — Remove ANSI escape sequences that winget emits to colorize the terminal.
        var clean = AnsiRegex.Replace(wingetOutput, string.Empty);

        // 2 — Normalize line endings and split.
        var lines = clean
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n');

        // 3 — Find the header line that contains the column names.
        int headerIdx = FindHeaderLine(lines);
        if (headerIdx < 0)
            return Array.Empty<AppUpdateItem>();

        // 4 — Determine where each column starts based on the header text.
        if (!TryGetColumnPositions(lines[headerIdx], out var cols))
            return Array.Empty<AppUpdateItem>();

        // 5 — Parse data rows that appear after the separator line.
        //     The separator is the line of dashes immediately after the header.
        var results = new List<AppUpdateItem>();
        int dataStart = headerIdx + 2; // skip header + separator

        for (int i = dataStart; i < lines.Length; i++)
        {
            var line = lines[i];

            // Skip empty lines, separator lines, and footer summary lines.
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith('-')) continue;
            if (FooterRegex.IsMatch(line.Trim())) continue;

            // A data row must be at least long enough to reach the Available column.
            if (line.Length <= cols.Available) continue;

            var item = TryParseLine(line, cols);
            if (item is not null)
                results.Add(item);
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns the index of the header line, or -1 if not found.</summary>
    private static int FindHeaderLine(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            // A header line contains all four expected column names.
            if (l.Contains("Name") && l.Contains("Id") &&
                l.Contains("Version") && l.Contains("Available"))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Holds the zero-based start index of each column in a data row.
    /// </summary>
    private readonly record struct ColumnPositions(int Id, int Version, int Available, int Source);

    /// <summary>
    /// Reads column start positions from the header line text.
    /// Returns false when any mandatory column cannot be located.
    /// </summary>
    private static bool TryGetColumnPositions(string header, out ColumnPositions cols)
    {
        cols = default;

        int id        = header.IndexOf("Id",        StringComparison.Ordinal);
        int version   = header.IndexOf("Version",   StringComparison.Ordinal);
        int available = header.IndexOf("Available", StringComparison.Ordinal);
        int source    = header.IndexOf("Source",    StringComparison.Ordinal);

        // Id, Version, Available are mandatory. Source is optional (may be -1).
        if (id < 0 || version < 0 || available < 0)
            return false;

        cols = new ColumnPositions(id, version, available, source < 0 ? int.MaxValue : source);
        return true;
    }

    /// <summary>
    /// Extracts one <see cref="AppUpdateItem"/> from a single data row.
    /// Returns null if the row is too short, empty, or missing required fields.
    /// </summary>
    private static AppUpdateItem? TryParseLine(string line, ColumnPositions cols)
    {
        try
        {
            string name      = Slice(line, 0,             cols.Id).Trim();
            string id        = Slice(line, cols.Id,       cols.Version).Trim();
            string version   = Slice(line, cols.Version,  cols.Available).Trim();
            string available = Slice(line, cols.Available, cols.Source).Trim();

            // Both name and id are required.
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                return null;

            // Skip rows where no update version is available.
            if (string.IsNullOrWhiteSpace(available) ||
                available.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return null;

            return new AppUpdateItem
            {
                Id             = id,
                DisplayName    = name,
                WingetPackageId = id,
                CurrentVersion = string.IsNullOrWhiteSpace(version) ? "Unknown" : version,
                AvailableVersion = available
            };
        }
        catch
        {
            // Never let a single bad row crash the whole parse.
            return null;
        }
    }

    /// <summary>
    /// Returns the substring of <paramref name="line"/> from <paramref name="start"/>
    /// to <paramref name="end"/>, clamped safely to the actual string length.
    /// </summary>
    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length) return string.Empty;
        int safeEnd = Math.Min(end, line.Length);
        return line[start..safeEnd];
    }
}
