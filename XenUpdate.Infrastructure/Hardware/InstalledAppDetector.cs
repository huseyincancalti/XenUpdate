using XenUpdate.Core.Interfaces;

namespace XenUpdate.Infrastructure.Hardware;

/// <summary>Detects installed vendor tools by expanding and probing candidate executable paths.</summary>
public sealed class InstalledAppDetector : IInstalledAppDetector
{
    /// <inheritdoc />
    public string? FindExecutable(IEnumerable<string> exePathCandidates)
    {
        foreach (var candidate in exePathCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate);
                if (File.Exists(expanded))
                    return expanded;
            }
            catch
            {
                // Ignore malformed candidate paths.
            }
        }

        return null;
    }
}
