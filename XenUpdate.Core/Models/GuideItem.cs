using CommunityToolkit.Mvvm.ComponentModel;
using XenUpdate.Core.Enums;

namespace XenUpdate.Core.Models;

/// <summary>
/// A single guided manual-update entry: something the user should update by hand because it
/// cannot be (or should not be) updated automatically — e.g. a GPU driver or a BIOS. Static
/// fields come from the embedded catalog; <see cref="IsCompleted"/> is runtime state the user sets.
/// </summary>
public sealed partial class GuideItem : ObservableObject
{
    /// <summary>Stable identifier used to persist completion (e.g. "gpu-nvidia").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Short, user-facing title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Plain-language explanation of why this update matters.</summary>
    public string Why { get; init; } = string.Empty;

    /// <summary>The category this guide belongs to.</summary>
    public GuideCategory Category { get; init; }

    /// <summary>Official vendor URL where the user downloads the update.</summary>
    public string OfficialUrl { get; init; } = string.Empty;

    /// <summary>Ordered, step-by-step instructions.</summary>
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();

    /// <summary>
    /// When set, the guide is only shown if the detected primary GPU vendor matches
    /// (e.g. "NVIDIA"). Null/empty means the guide always applies.
    /// </summary>
    public string? RequiredGpuVendor { get; init; }

    /// <summary>True once the user has marked the guide done. Persisted across sessions.</summary>
    [ObservableProperty]
    private bool _isCompleted;
}
