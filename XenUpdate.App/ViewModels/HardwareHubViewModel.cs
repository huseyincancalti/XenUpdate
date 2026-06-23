using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using XenUpdate.App.Services;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// The guided-update center: detects the machine's CPU/GPU, then surfaces the applicable guides
/// as interactive step-by-step cards (with an adaptive "open the installed vendor tool" action).
/// This is the product's differentiating feature — manual updates that can't be automated.
/// </summary>
public sealed partial class HardwareHubViewModel : ObservableObject
{
    private readonly IHardwareScannerService _hardwareScanner;
    private readonly IGuideCatalog _guideCatalog;
    private readonly IGuideCompletionStore _completionStore;
    private readonly IInstalledAppDetector _appDetector;
    private readonly INvidiaDriverService _nvidiaService;
    private readonly ILoggerService _logger;

    private bool _initialized;
    private DriverUpdateStatus? _nvidiaStatusCache;

    [ObservableProperty]
    private HardwareProfile _hardware = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Set when a GPU driver was checked and found current — a reassuring "you're up to date" note.</summary>
    [ObservableProperty]
    private string? _currentDriverNote;

    public bool HasCurrentDriverNote => !string.IsNullOrWhiteSpace(CurrentDriverNote);

    partial void OnCurrentDriverNoteChanged(string? value) => OnPropertyChanged(nameof(HasCurrentDriverNote));

    /// <summary>The interactive guide cards that apply to the detected hardware.</summary>
    public ObservableCollection<GuideCardViewModel> Guides { get; } = new();

    public bool HasGuides => Guides.Count > 0;

    public bool ShowEmptyState => !IsLoading && !HasGuides;

    public HardwareHubViewModel(
        IHardwareScannerService hardwareScanner,
        IGuideCatalog guideCatalog,
        IGuideCompletionStore completionStore,
        IInstalledAppDetector appDetector,
        INvidiaDriverService nvidiaService,
        ILoggerService logger)
    {
        _hardwareScanner = hardwareScanner;
        _guideCatalog = guideCatalog;
        _completionStore = completionStore;
        _appDetector = appDetector;
        _nvidiaService = nvidiaService;
        _logger = logger;

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged() =>
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            try { await LoadGuidesAsync(); }
            catch (Exception ex) { _logger.Error("Reloading guides after language change failed.", ex); }
        });

    /// <summary>Detects hardware and loads the applicable guides. Called from the view's Loaded event.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        IsLoading = true;
        StatusMessage = LocalizationManager.Instance["StatusDetectingHardware"];

        try
        {
            Hardware = await _hardwareScanner.GetCurrentHardwareAsync();
            await LoadGuidesAsync();
            _initialized = true;
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.Instance["StatusGuidesFailed"];
            _logger.Error("Guide center initialization failed.", ex);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    private async Task LoadGuidesAsync()
    {
        var all = await _guideCatalog.GetGuidesAsync(LocalizationManager.Instance.CurrentLanguage);
        var completed = await _completionStore.GetCompletedIdsAsync();
        var completedSet = new HashSet<string>(completed, StringComparer.OrdinalIgnoreCase);

        CurrentDriverNote = null;
        Guides.Clear();
        foreach (var guide in all.Where(AppliesToCurrentHardware))
        {
            var appPath = guide.AppLaunch is { ExeCandidates.Count: > 0 }
                ? _appDetector.FindExecutable(guide.AppLaunch.ExeCandidates)
                : null;

            DriverUpdateStatus? status = null;
            if (string.Equals(guide.RequiredGpuVendor, "NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                status = _nvidiaStatusCache ??= await _nvidiaService.CheckAsync();
                // If we reliably know the driver is current, don't show a guide — show a
                // reassuring "you're up to date" note instead so the page never looks broken.
                if (status.Checked && !status.UpdateAvailable)
                {
                    CurrentDriverNote = string.Format(LocalizationManager.Instance["DriverCurrent"], status.InstalledVersion);
                    continue;
                }
            }

            Guides.Add(new GuideCardViewModel(guide, appPath, completedSet, status, _completionStore, _logger));
        }

        OnPropertyChanged(nameof(HasGuides));
        OnPropertyChanged(nameof(ShowEmptyState));

        StatusMessage = HasGuides
            ? string.Format(LocalizationManager.Instance["StatusGuidesCount"], Guides.Count)
            : LocalizationManager.Instance["StatusGuidesNone"];
    }

    private bool AppliesToCurrentHardware(GuideItem guide)
    {
        if (string.IsNullOrWhiteSpace(guide.RequiredGpuVendor))
            return true;

        return string.Equals(guide.RequiredGpuVendor, Hardware.GpuVendor, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));
}
