using CommunityToolkit.Mvvm.ComponentModel;

namespace XenUpdate.App.ViewModels;

/// <summary>One interactive step in a guide: a number, its text, and whether it's done/active.</summary>
public sealed partial class GuideStepVm : ObservableObject
{
    /// <summary>Persistence key, e.g. "gpu-nvidia#2".</summary>
    public string StepId { get; }

    /// <summary>1-based display number.</summary>
    public int Number { get; }

    /// <summary>The instruction text.</summary>
    public string Text { get; }

    [ObservableProperty]
    private bool _isDone;

    /// <summary>True for the first not-yet-done step (the one the user should do next).</summary>
    [ObservableProperty]
    private bool _isActive;

    public GuideStepVm(string stepId, int number, string text, bool isDone)
    {
        StepId = stepId;
        Number = number;
        Text = text;
        _isDone = isDone;
    }
}
