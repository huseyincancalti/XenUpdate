namespace XenUpdate.Core.Models;

/// <summary>
/// A single progress update for an in-flight application install.
/// </summary>
/// <param name="Percent">
/// Completion percentage (0–100), or <c>-1</c> when winget has not reported a
/// usable percentage yet (e.g. before the download starts).
/// </param>
/// <param name="DownloadText">
/// Human-readable download size such as <c>"2.0 MB / 84.0 MB"</c> when winget is
/// downloading, or <c>null</c> when no size line is available. Units come straight
/// from winget, so this is locale-neutral.
/// </param>
public readonly record struct InstallProgress(int Percent, string? DownloadText = null);
