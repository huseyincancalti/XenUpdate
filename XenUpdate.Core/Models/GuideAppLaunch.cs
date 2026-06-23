namespace XenUpdate.Core.Models;

/// <summary>
/// Optional per-guide info for launching the vendor's own tool (e.g. NVIDIA App) when it is
/// already installed, so the guide can offer a one-click "open the app" action instead of the
/// web walkthrough.
/// </summary>
public sealed class GuideAppLaunch
{
    /// <summary>User-facing name of the tool (e.g. "NVIDIA App").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Candidate executable paths (may contain environment variables like %ProgramFiles%).
    /// The first one that exists on disk is used as the launch target.
    /// </summary>
    public IReadOnlyList<string> ExeCandidates { get; init; } = Array.Empty<string>();
}
