using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XenUpdate.App.Services;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// Interactive wrapper around a <see cref="GuideItem"/>: an ordered, checkable step list with
/// progress, a completion state, and an adaptive primary action — launch the installed vendor
/// tool when present, otherwise open the official web page. Step progress is persisted.
/// </summary>
public sealed partial class GuideCardViewModel : ObservableObject
{
    private readonly GuideItem _guide;
    private readonly IGuideCompletionStore _store;
    private readonly ILoggerService _logger;
    private readonly string? _appExePath;

    public string Title => _guide.Title;
    public string Why => _guide.Why;

    public ObservableCollection<GuideStepVm> Steps { get; } = new();

    /// <summary>Label for the primary button (e.g. "Open NVIDIA App" or "Open official page").</summary>
    public string PrimaryActionText { get; }

    [ObservableProperty]
    private int _doneCount;

    [ObservableProperty]
    private bool _isCompleted;

    public int TotalCount => Steps.Count;
    public int ProgressPercent => TotalCount == 0 ? 0 : (int)System.Math.Round(DoneCount * 100.0 / TotalCount);
    public string ProgressText => $"{DoneCount}/{TotalCount}";

    public GuideCardViewModel(
        GuideItem guide,
        string? appExePath,
        ISet<string> completedStepIds,
        IGuideCompletionStore store,
        ILoggerService logger)
    {
        _guide = guide;
        _appExePath = appExePath;
        _store = store;
        _logger = logger;

        var useAppFlow = appExePath is not null && guide.AppSteps.Count > 0;
        var rawSteps = useAppFlow ? guide.AppSteps : guide.Steps;

        PrimaryActionText = appExePath is not null && guide.AppLaunch is not null
            ? string.Format(LocalizationManager.Instance["OpenApp"], guide.AppLaunch.DisplayName)
            : LocalizationManager.Instance["OpenOfficialPage"];

        for (var i = 0; i < rawSteps.Count; i++)
        {
            var stepId = $"{guide.Id}#{i}";
            Steps.Add(new GuideStepVm(stepId, i + 1, rawSteps[i], completedStepIds.Contains(stepId)));
        }

        Recompute();
    }

    [RelayCommand]
    private async Task ToggleStep(GuideStepVm? step)
    {
        if (step is null)
            return;

        step.IsDone = !step.IsDone;
        await _store.SetCompletedAsync(step.StepId, step.IsDone);
        Recompute();
    }

    [RelayCommand]
    private void PrimaryAction()
    {
        var target = _appExePath ?? _guide.OfficialUrl;
        if (string.IsNullOrWhiteSpace(target))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            _logger.Error($"Could not launch guide target '{target}'.", ex);
        }
    }

    private void Recompute()
    {
        DoneCount = Steps.Count(s => s.IsDone);
        IsCompleted = TotalCount > 0 && DoneCount == TotalCount;

        var activeIndex = -1;
        for (var i = 0; i < Steps.Count; i++)
        {
            if (!Steps[i].IsDone)
            {
                activeIndex = i;
                break;
            }
        }

        for (var i = 0; i < Steps.Count; i++)
            Steps[i].IsActive = i == activeIndex;

        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(TotalCount));
    }
}
