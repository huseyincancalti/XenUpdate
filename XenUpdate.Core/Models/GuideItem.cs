using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// A guided manual-update entry from the catalog: something the user should update by hand
/// because it cannot be (or should not be) automated — e.g. a GPU driver or a BIOS. This is a
/// plain data record; runtime/interaction state lives in the view model that wraps it.
/// </summary>
public sealed class GuideItem
{
    /// <summary>Stable identifier used to persist progress (e.g. "gpu-nvidia").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Short, user-facing title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Compact label for tight spaces (the sidebar sub-branch) — a vendor/product name like
    /// "NVIDIA" or "Visual Studio", not the full sentence-style <see cref="Title"/>.
    /// </summary>
    public string ShortLabel { get; init; } = string.Empty;

    /// <summary>Plain-language explanation of why this update matters.</summary>
    public string Why { get; init; } = string.Empty;

    /// <summary>The category this guide belongs to.</summary>
    public GuideCategory Category { get; init; }

    /// <summary>Official vendor URL where the user downloads the update (the web fallback).</summary>
    public string OfficialUrl { get; init; } = string.Empty;

    /// <summary>Step-by-step instructions for the web/manual path.</summary>
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Alternative steps shown when the vendor tool (<see cref="AppLaunch"/>) is installed.
    /// Empty means: reuse <see cref="Steps"/>.
    /// </summary>
    public IReadOnlyList<string> AppSteps { get; init; } = Array.Empty<string>();

    /// <summary>
    /// When set, the guide only applies if the detected primary GPU vendor matches
    /// (e.g. "NVIDIA"). Null/empty means it always applies.
    /// </summary>
    public string? RequiredGpuVendor { get; init; }

    /// <summary>
    /// When set, identifies which real version-check service should verify this guide's
    /// up-to-date state (e.g. "VisualStudio"). Null means no automatic check exists for this
    /// guide — it always shows its step wizard, never an "up to date" reassurance state,
    /// since there is nothing to compare the installed version against.
    /// </summary>
    public string? VersionCheckKind { get; init; }

    /// <summary>Optional: the vendor tool to open directly when it is installed.</summary>
    public GuideAppLaunch? AppLaunch { get; init; }
}
