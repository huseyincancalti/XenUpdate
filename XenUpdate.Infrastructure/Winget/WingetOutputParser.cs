using System.Text.RegularExpressions;
using XenUpdate.Core.Models;

namespace XenUpdate.Infrastructure.Winget;

/// <summary>
/// Parses the raw text output of "winget upgrade" into <see cref="AppUpdateItem"/> objects.
/// Pure (string in, list out) so it is fully unit-testable without spawning a process.
///
/// Columns are located by POSITION, not by matching English words: the table header is the
/// line directly above winget's dashed separator, and each column starts where a whitespace-
/// delimited token starts in that header. This keeps parsing working on localized Windows
/// (e.g. Turkish "Ad / Kimlik / Sürüm / Kullanılabilir / Kaynak"), where matching the literal
/// word "Available" would fail and silently return zero updates.
/// </summary>
public sealed class WingetOutputParser
{
    // VT100/ANSI escape sequences winget emits to colorize the terminal (e.g. \x1b[31m).
    private static readonly Regex AnsiRegex =
        new(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    /// <summary>Parses raw "winget upgrade" stdout into update items; unreadable rows are skipped.</summary>
    public IReadOnlyList<AppUpdateItem> Parse(string wingetOutput)
    {
        if (string.IsNullOrWhiteSpace(wingetOutput))
            return Array.Empty<AppUpdateItem>();

        var clean = AnsiRegex.Replace(wingetOutput, string.Empty);

        var lines = clean
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n');

        // The header is the line directly above the dashed separator row.
        int separatorIdx = FindSeparatorLine(lines);
        if (separatorIdx < 1)
            return Array.Empty<AppUpdateItem>();

        if (!TryGetColumnPositions(lines[separatorIdx - 1], out var cols))
            return Array.Empty<AppUpdateItem>();

        var results = new List<AppUpdateItem>();

        for (int i = separatorIdx + 1; i < lines.Length; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (IsSeparatorLine(line)) continue;
            // A data row must reach at least the Available column to carry a target version.
            if (line.Length <= cols.Available) continue;

            var item = TryParseLine(line, cols);
            if (item is not null)
                results.Add(item);
        }

        return results;
    }

    /// <summary>Index of winget's dashed separator row (a run of '-'), or -1 if absent.</summary>
    private static int FindSeparatorLine(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (IsSeparatorLine(lines[i]))
                return i;
        }
        return -1;
    }

    private static bool IsSeparatorLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 10 && trimmed.All(c => c == '-');
    }

    /// <summary>Zero-based start index of each column in a data row.</summary>
    private readonly record struct ColumnPositions(int Id, int Version, int Available, int Source);

    /// <summary>
    /// Derives column start offsets from the header by locating whitespace-delimited tokens
    /// and mapping them by ORDER: Name, Id, Version, Available, [Source]. Source is optional.
    /// Returns false when fewer than four columns are present.
    /// </summary>
    private static bool TryGetColumnPositions(string header, out ColumnPositions cols)
    {
        cols = default;

        var starts = new List<int>();
        for (int i = 0; i < header.Length; i++)
        {
            bool isTokenStart = !char.IsWhiteSpace(header[i]) &&
                                (i == 0 || char.IsWhiteSpace(header[i - 1]));
            if (isTokenStart)
                starts.Add(i);
        }

        // Need at least Name, Id, Version, Available.
        if (starts.Count < 4)
            return false;

        int source = starts.Count > 4 ? starts[4] : int.MaxValue;
        cols = new ColumnPositions(Id: starts[1], Version: starts[2], Available: starts[3], Source: source);
        return true;
    }

    /// <summary>
    /// Extracts one <see cref="AppUpdateItem"/> from a data row, or null when the row lacks a
    /// package id or an upgrade target (empty / "Unknown" available version).
    /// </summary>
    private static AppUpdateItem? TryParseLine(string line, ColumnPositions cols)
    {
        try
        {
            string name      = Slice(line, 0,              cols.Id).Trim();
            string id        = Slice(line, cols.Id,        cols.Version).Trim();
            string version   = Slice(line, cols.Version,   cols.Available).Trim();
            string available = Slice(line, cols.Available, cols.Source).Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                return null;

            if (string.IsNullOrWhiteSpace(available) ||
                available.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return null;

            return new AppUpdateItem
            {
                Id               = id,
                DisplayName      = name,
                WingetPackageId  = id,
                CurrentVersion   = string.IsNullOrWhiteSpace(version) ? "Unknown" : version,
                AvailableVersion = available
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Substring from <paramref name="start"/> to <paramref name="end"/>, clamped to length.</summary>
    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length) return string.Empty;
        int safeEnd = Math.Min(end, line.Length);
        return line[start..safeEnd];
    }
}
