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
/// <param name="FailureReason">
/// Non-null only on the final progress event when the install fails. A stable key
/// (e.g. <c>"NoInternet"</c>, <c>"NeedsAdmin"</c>) rather than a human sentence, so the
/// UI layer — the only layer that knows the active language — can localize it via
/// <c>LocalizationManager["WingetFail_" + FailureReason]</c>. Infrastructure has no
/// UI dependency and must not decide user-facing wording.
/// </param>
/// <param name="FailureDetail">
/// Extra data to interpolate into the localized failure message, when the message has a
/// placeholder (e.g. the raw exit code for an unmapped failure, or a timeout duration).
/// Null when the failure key's localized string takes no arguments.
/// </param>
public readonly record struct InstallProgress(
    int Percent,
    string? DownloadText = null,
    string? FailureReason = null,
    string? FailureDetail = null);
