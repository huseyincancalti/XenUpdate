using System.Text.RegularExpressions;

namespace XenUpdate.Infrastructure.Winget;

/// <summary>
/// Extracts download size and completion percentage from a single line of winget's
/// console output. winget redraws a progress bar using carriage returns, so when stdout
/// is read line-by-line each redraw arrives as its own line such as
/// <c>"  █████▒▒▒▒  2.00 MB / 3.07 MB"</c> or <c>"  ██████  42%"</c>.
/// </summary>
internal static class WingetProgressParser
{
    // "<downloaded> <unit> / <total> <unit>", e.g. "1024 KB / 3.07 MB".
    private static readonly Regex DownloadRegex = new(
        @"(\d+(?:[.,]\d+)?)\s*(B|KB|MB|GB|TB)\s*/\s*(\d+(?:[.,]\d+)?)\s*(B|KB|MB|GB|TB)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // A percentage token such as "42%". The last one on a line is the freshest.
    private static readonly Regex PercentRegex = new(
        @"(\d{1,3})\s*%",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Tries to read a "downloaded / total" size from the line. On success, <paramref name="text"/>
    /// is normalized to a compact form like <c>"2.00 MB / 3.07 MB"</c>.
    /// </summary>
    public static bool TryParseDownload(string line, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var match = DownloadRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var downloaded = $"{match.Groups[1].Value} {match.Groups[2].Value.ToUpperInvariant()}";
        var total = $"{match.Groups[3].Value} {match.Groups[4].Value.ToUpperInvariant()}";
        text = $"{downloaded} / {total}";
        return true;
    }

    /// <summary>
    /// Tries to read a completion percentage (0–100) from the line, taking the last token
    /// when several are present.
    /// </summary>
    public static bool TryParsePercent(string line, out int percent)
    {
        percent = -1;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var matches = PercentRegex.Matches(line);
        if (matches.Count == 0)
        {
            return false;
        }

        if (!int.TryParse(matches[^1].Groups[1].Value, out var value))
        {
            return false;
        }

        percent = Math.Clamp(value, 0, 100);
        return true;
    }
}
