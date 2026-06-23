using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XenUpdate.App.Services;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// Drives one guide as a step-by-step wizard: the user moves through one focused step at a time
/// with Next / Previous, an overview dot strip shows progress, and the primary action launches the
/// installed vendor tool (auto-completing its step) or opens the official page. Progress persists.
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

    /// <summary>Label for the launch button (e.g. "Open NVIDIA App" or "Open official page").</summary>
    public string PrimaryActionText { get; }

    /// <summary>True when there is an app or URL to open from the launch step.</summary>
    public bool HasLaunchTarget { get; }

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private bool _isCompleted;

    public int TotalCount => Steps.Count;

    public GuideStepVm? CurrentStep =>
        CurrentIndex >= 0 && CurrentIndex < Steps.Count ? Steps[CurrentIndex] : null;

    public int ProgressPercent =>
        TotalCount == 0 ? 0 : (int)System.Math.Round(Steps.Count(s => s.IsDone) * 100.0 / TotalCount);

    public string StepCounter =>
        string.Format(LocalizationManager.Instance["StepCounter"], CurrentIndex + 1, TotalCount);

    public bool ShowLaunchButton => CurrentIndex == 0 && !IsCompleted && HasLaunchTarget;
    public bool CanGoPrevious => CurrentIndex > 0 && !IsCompleted;
    public bool ShowNavigation => !IsCompleted;

    public string NextButtonText =>
        LocalizationManager.Instance[CurrentIndex >= TotalCount - 1 ? "FinishGuide" : "NextStep"];

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

        HasLaunchTarget = appExePath is not null || !string.IsNullOrWhiteSpace(guide.OfficialUrl);
        PrimaryActionText = appExePath is not null && guide.AppLaunch is not null
            ? string.Format(LocalizationManager.Instance["OpenApp"], guide.AppLaunch.DisplayName)
            : LocalizationManager.Instance["OpenOfficialPage"];

        for (var i = 0; i < rawSteps.Count; i++)
        {
            var stepId = $"{guide.Id}#{i}";
            Steps.Add(new GuideStepVm(stepId, i + 1, rawSteps[i], completedStepIds.Contains(stepId)));
        }

        // Resume at the first step that isn't done yet.
        var firstUndone = -1;
        for (var i = 0; i < Steps.Count; i++)
        {
            if (!Steps[i].IsDone) { firstUndone = i; break; }
        }
        CurrentIndex = firstUndone < 0 ? System.Math.Max(0, Steps.Count - 1) : firstUndone;
        Recompute();
    }

    [RelayCommand]
    private async Task NextStep()
    {
        await MarkDoneAsync(CurrentIndex);
        if (CurrentIndex < Steps.Count - 1)
            CurrentIndex++;
        Recompute();
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
            Recompute();
        }
    }

    [RelayCommand]
    private async Task Launch()
    {
        var target = _appExePath ?? _guide.OfficialUrl;
        if (!string.IsNullOrWhiteSpace(target))
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                _logger.Error($"Could not launch guide target '{target}'.", ex);
            }
        }

        // We opened it for them, so the launch step is done — advance to keep the flow moving.
        await MarkDoneAsync(0);
        if (CurrentIndex == 0 && Steps.Count > 1)
            CurrentIndex = 1;
        Recompute();
    }

    [RelayCommand]
    private async Task Restart()
    {
        foreach (var step in Steps)
        {
            if (step.IsDone)
            {
                step.IsDone = false;
                await _store.SetCompletedAsync(step.StepId, false);
            }
        }

        CurrentIndex = 0;
        Recompute();
    }

    private async Task MarkDoneAsync(int index)
    {
        if (index < 0 || index >= Steps.Count || Steps[index].IsDone)
            return;

        Steps[index].IsDone = true;
        await _store.SetCompletedAsync(Steps[index].StepId, true);
    }

    private void Recompute()
    {
        IsCompleted = TotalCount > 0 && Steps.All(s => s.IsDone);

        for (var i = 0; i < Steps.Count; i++)
            Steps[i].IsActive = i == CurrentIndex && !IsCompleted;

        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StepCounter));
        OnPropertyChanged(nameof(ShowLaunchButton));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(ShowNavigation));
        OnPropertyChanged(nameof(NextButtonText));
    }
}
