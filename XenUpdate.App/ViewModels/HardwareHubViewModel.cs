using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XenUpdate.App.Services;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// The guided-update center: detects the machine's CPU/GPU, then surfaces the catalog guides
/// that apply (e.g. the NVIDIA driver guide on an NVIDIA machine) with their completion state.
/// This is the product's differentiating feature — manual updates that can't be automated.
/// </summary>
public sealed partial class HardwareHubViewModel : ObservableObject
{
    private readonly IHardwareScannerService _hardwareScanner;
    private readonly IGuideCatalog _guideCatalog;
    private readonly IGuideCompletionStore _completionStore;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private HardwareProfile _hardware = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Guides that apply to the detected hardware.</summary>
    public ObservableCollection<GuideItem> Guides { get; } = new();

    /// <summary>True when at least one guide applies.</summary>
    public bool HasGuides => Guides.Count > 0;

    /// <summary>True once loading is done and no guides apply; drives the empty-state panel.</summary>
    public bool ShowEmptyState => !IsLoading && !HasGuides;

    public HardwareHubViewModel(
        IHardwareScannerService hardwareScanner,
        IGuideCatalog guideCatalog,
        IGuideCompletionStore completionStore,
        ILoggerService logger)
    {
        _hardwareScanner = hardwareScanner;
        _guideCatalog = guideCatalog;
        _completionStore = completionStore;
        _logger = logger;

        // Reload guides in the chosen language when the user switches languages.
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged() =>
        Application.Current?.Dispatcher.InvokeAsync(async () => await LoadGuidesAsync());

    /// <summary>Detects hardware and loads the applicable guides. Called from the view's Loaded event.</summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Detecting hardware…";

        try
        {
            Hardware = await _hardwareScanner.GetCurrentHardwareAsync();
            await LoadGuidesAsync();

            StatusMessage = HasGuides
                ? $"{Guides.Count} guided update(s) for your hardware."
                : "No guided updates apply to your hardware right now.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load guides: {ex.Message}";
            _logger.Error("Guide center initialization failed.", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadGuidesAsync()
    {
        var all = await _guideCatalog.GetGuidesAsync(LocalizationManager.Instance.CurrentLanguage);
        var completed = await _completionStore.GetCompletedIdsAsync();
        var completedSet = new HashSet<string>(completed, StringComparer.OrdinalIgnoreCase);

        Guides.Clear();
        foreach (var guide in all.Where(AppliesToCurrentHardware))
        {
            guide.IsCompleted = completedSet.Contains(guide.Id);
            Guides.Add(guide);
        }

        OnPropertyChanged(nameof(HasGuides));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    private bool AppliesToCurrentHardware(GuideItem guide)
    {
        if (string.IsNullOrWhiteSpace(guide.RequiredGpuVendor))
            return true;

        return string.Equals(guide.RequiredGpuVendor, Hardware.GpuVendor, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void OpenGuide(GuideItem? guide)
    {
        if (guide is null || string.IsNullOrWhiteSpace(guide.OfficialUrl))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(guide.OfficialUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open guide URL '{guide.OfficialUrl}'.", ex);
        }
    }

    [RelayCommand]
    private async Task ToggleComplete(GuideItem? guide)
    {
        if (guide is null)
            return;

        guide.IsCompleted = !guide.IsCompleted;
        await _completionStore.SetCompletedAsync(guide.Id, guide.IsCompleted);
    }
}
