using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using XenUpdate.App.Services;
using XenUpdate.Core.Enums;
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
    private readonly IVisualStudioUpdateService _visualStudioService;
    private readonly ILoggerService _logger;

    private bool _initialized;
    private DriverUpdateStatus? _nvidiaStatusCache;
    private DriverUpdateStatus? _visualStudioStatusCache;
    private DateTime _lastDynamicRefresh = DateTime.MinValue;

    // Throttles RefreshAfterPossibleExternalChangeAsync so rapid window-activation events
    // (alt-tabbing, dismissing a menu) don't hammer the NVIDIA check repeatedly.
    private static readonly TimeSpan DynamicRefreshThrottle = TimeSpan.FromSeconds(20);

    [ObservableProperty]
    private HardwareProfile _hardware = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Tracks the logical selection across reloads: LoadGuidesAsync rebuilds every
    // GuideCardViewModel from scratch, so the *instance* the user was looking at goes stale
    // the moment a background re-verification runs. Re-resolving by id keeps them on the same
    // guide (now possibly showing an updated state) instead of being silently bounced to the
    // landing page mid-read.
    private string? _selectedGuideId;

    /// <summary>
    /// The guide currently shown in detail, or null when the landing (explanation + entry
    /// cards) should show instead.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingGuideDetail))]
    private GuideCardViewModel? _selectedGuide;

    /// <summary>True when a specific guide's step wizard is showing instead of the landing.</summary>
    public bool IsShowingGuideDetail => SelectedGuide is not null;

    /// <summary>The interactive guide cards that apply to the detected hardware.</summary>
    public ObservableCollection<GuideCardViewModel> Guides { get; } = new();

    /// <summary>
    /// The currently-applicable guides' short labels (e.g. "NVIDIA", "Visual Studio"), in
    /// display order. Bound by the sidebar so "Guides" shows exactly which guides apply as
    /// sub-branches, before the user ever opens the page.
    /// </summary>
    public ObservableCollection<GuideSidebarItem> SidebarGuides { get; } = new();

    public bool HasGuides => Guides.Count > 0;

    /// <summary>
    /// How many applicable guides still need action (excludes ones a real check already
    /// confirmed are up to date). Bound by the Overview page's quick-stat chip.
    /// </summary>
    public int GuidesNeedingAttentionCount => Guides.Count(g => !g.IsUpToDate);

    public bool ShowEmptyState => !IsLoading && !HasGuides;

    public HardwareHubViewModel(
        IHardwareScannerService hardwareScanner,
        IGuideCatalog guideCatalog,
        IGuideCompletionStore completionStore,
        IInstalledAppDetector appDetector,
        INvidiaDriverService nvidiaService,
        IVisualStudioUpdateService visualStudioService,
        ILoggerService logger)
    {
        _hardwareScanner = hardwareScanner;
        _guideCatalog = guideCatalog;
        _completionStore = completionStore;
        _appDetector = appDetector;
        _nvidiaService = nvidiaService;
        _visualStudioService = visualStudioService;
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

        Guides.Clear();
        foreach (var guide in all)
        {
            var appPath = guide.AppLaunch is { ExeCandidates.Count: > 0 }
                ? _appDetector.FindExecutable(guide.AppLaunch.ExeCandidates)
                : null;

            if (!AppliesToCurrentMachine(guide, appPath))
                continue;

            DriverUpdateStatus? status = null;
            if (string.Equals(guide.RequiredGpuVendor, "NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                // Only cache successful checks. A network failure returns Checked=false and must
                // not be cached; otherwise a transient outage permanently hides the "up to date"
                // state until the app restarts.
                if (_nvidiaStatusCache is null)
                {
                    var fresh = await _nvidiaService.CheckAsync();
                    if (fresh.Checked)
                        _nvidiaStatusCache = fresh;
                    status = fresh;
                }
                else
                {
                    status = _nvidiaStatusCache;
                }
            }
            else if (string.Equals(guide.VersionCheckKind, "VisualStudio", StringComparison.OrdinalIgnoreCase))
            {
                if (_visualStudioStatusCache is null)
                {
                    var fresh = await _visualStudioService.CheckAsync();
                    if (fresh.Checked)
                        _visualStudioStatusCache = fresh;
                    status = fresh;
                }
                else
                {
                    status = _visualStudioStatusCache;
                }
            }

            // Always add the card, current or not — GuideCardViewModel.IsUpToDate drives which
            // state the detail page and sidebar/landing entries show. A guide that applies to
            // this machine never just vanishes because nothing needs doing right now.
            var card = new GuideCardViewModel(guide, appPath, completedSet, status, _completionStore, _logger);
            // Completion here is the user's own step checklist — self-reported, not proof the
            // update actually happened. When it flips true, trigger a real re-verification
            // (currently: the NVIDIA driver version check) instead of trusting it at face value.
            card.PropertyChanged += OnGuideCardPropertyChanged;
            Guides.Add(card);
        }

        RefreshSidebarGuides();

        // Re-resolve the selection by id: the cards were just rebuilt from scratch, so the old
        // SelectedGuide instance is stale even if the same logical guide still exists.
        SelectedGuide = _selectedGuideId is null
            ? null
            : Guides.FirstOrDefault(g => string.Equals(g.Id, _selectedGuideId, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(HasGuides));
        OnPropertyChanged(nameof(GuidesNeedingAttentionCount));
        OnPropertyChanged(nameof(ShowEmptyState));

        StatusMessage = HasGuides
            ? string.Format(LocalizationManager.Instance["StatusGuidesCount"], Guides.Count)
            : LocalizationManager.Instance["StatusGuidesNone"];
    }

    /// <summary>Shows a specific guide's detail (step wizard or up-to-date state) by its catalog id.</summary>
    public void SelectGuideById(string id)
    {
        _selectedGuideId = id;
        SelectedGuide = Guides.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        UpdateSidebarActiveStates();
    }

    /// <summary>Returns to the landing (explanation + entry cards), leaving Guides selected in the sidebar.</summary>
    public void ShowLanding()
    {
        _selectedGuideId = null;
        SelectedGuide = null;
        UpdateSidebarActiveStates();
    }

    private void RefreshSidebarGuides()
    {
        SidebarGuides.Clear();
        foreach (var card in Guides)
        {
            if (!string.IsNullOrWhiteSpace(card.ShortLabel))
                SidebarGuides.Add(new GuideSidebarItem(card.Id, card.ShortLabel));
        }

        UpdateSidebarActiveStates();
    }

    // Marks exactly the sub-branch matching the current selection as active, so the sidebar can
    // highlight it distinctly from its siblings instead of every branch looking identical at rest.
    private void UpdateSidebarActiveStates()
    {
        foreach (var item in SidebarGuides)
        {
            item.IsActive = _selectedGuideId is not null &&
                            string.Equals(item.Id, _selectedGuideId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnGuideCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuideCardViewModel.IsCompleted) && sender is GuideCardViewModel { IsCompleted: true })
        {
            _ = RefreshAfterPossibleExternalChangeAsync();
        }
    }

    /// <summary>
    /// Forces a fresh dynamic check (bypassing the NVIDIA cache) and reloads guides, so an
    /// update the user just performed — outside the app, or by finishing a guide's steps —
    /// is reflected without needing an app restart. Call this when the window regains focus
    /// or a guide's steps are finished; not on every ordinary page navigation, since that
    /// would hit the network check far more often than needed.
    /// </summary>
    public async Task RefreshAfterPossibleExternalChangeAsync()
    {
        if (!_initialized)
            return;

        if (DateTime.UtcNow - _lastDynamicRefresh < DynamicRefreshThrottle)
            return;

        _lastDynamicRefresh = DateTime.UtcNow;
        _nvidiaStatusCache = null;
        _visualStudioStatusCache = null;

        try
        {
            await LoadGuidesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Dynamic guide re-verification failed.", ex);
        }
    }

    /// <summary>
    /// Decides whether a catalog guide should appear on this machine:
    ///   - GPU-vendor guides only apply when the detected GPU vendor matches.
    ///   - Software guides with a launch target only apply when that app is actually
    ///     installed (<paramref name="appExePath"/> was resolved) — showing a guide for
    ///     software the user doesn't have would just be clutter, not "hard to find" the
    ///     one guide that does apply.
    ///   - Anything else always applies.
    /// </summary>
    private bool AppliesToCurrentMachine(GuideItem guide, string? appExePath)
    {
        if (!string.IsNullOrWhiteSpace(guide.RequiredGpuVendor))
            return string.Equals(guide.RequiredGpuVendor, Hardware.GpuVendor, StringComparison.OrdinalIgnoreCase);

        if (guide.Category == GuideCategory.Software && guide.AppLaunch is { ExeCandidates.Count: > 0 })
            return appExePath is not null;

        return true;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));
}

/// <summary>
/// A single applicable guide shown as a sidebar sub-branch under "Guides". <see cref="IsActive"/>
/// is mutable (not a plain record) so the sidebar can highlight exactly the one branch whose
/// detail is currently showing, updated in place by <see cref="HardwareHubViewModel.SelectGuideById"/>
/// and <see cref="HardwareHubViewModel.ShowLanding"/> without needing to rebuild the whole list.
/// </summary>
public sealed partial class GuideSidebarItem : ObservableObject
{
    public string Id { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;

    public GuideSidebarItem(string id, string label)
    {
        Id = id;
        Label = label;
    }
}
