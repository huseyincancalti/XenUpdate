namespace XenUpdate.Core.Interfaces;

/// <summary>Detects whether a vendor tool is installed by probing candidate executable paths.</summary>
public interface IInstalledAppDetector
{
    /// <summary>
    /// Expands environment variables in each candidate path and returns the first one that exists
    /// on disk, or <see langword="null"/> when none are found.
    /// </summary>
    string? FindExecutable(IEnumerable<string> exePathCandidates);
}
