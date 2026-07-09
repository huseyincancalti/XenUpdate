using CommunityToolkit.Mvvm.ComponentModel;
using XenUpdate.App.Services;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// The app's home page (<see cref="AppPage.Overview"/>, shown by default on startup):
/// detected hardware plus a live glance at what needs attention on every other page, so the
/// user can tell "is anything wrong right now?" without clicking into each one first.
/// Holds no update state of its own — every stat reads straight from the page ViewModel that
/// owns it, so there is exactly one source of truth to keep in sync.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly HardwareHubViewModel _hardwareHubVm;

    public ProgramsViewModel Programs { get; }
    public WindowsUpdatesViewModel WindowsUpdates { get; }
    public DriversViewModel Drivers { get; }
    public PipPackagesViewModel PipPackages { get; }

    /// <summary>The Guides page VM — its guide count and per-guide up-to-date state drive the guides chip.</summary>
    public HardwareHubViewModel Guides => _hardwareHubVm;

    /// <summary>Detected hardware, mirrored from the Guides page so it shows here without navigating there.</summary>
    public HardwareProfile Hardware => _hardwareHubVm.Hardware;

    public string ProgramsSummary => FormatUpdateCount(Programs.Updates.Count);
    public string WindowsUpdatesSummary => FormatUpdateCount(WindowsUpdates.Updates.Count);
    public string DriversSummary => FormatUpdateCount(Drivers.Updates.Count);
    public string PipPackagesSummary => FormatUpdateCount(PipPackages.Updates.Count);

    public string GuidesSummary => _hardwareHubVm.GuidesNeedingAttentionCount > 0
        ? string.Format(LocalizationManager.Instance["OverviewGuidesNeedingAttention"], _hardwareHubVm.GuidesNeedingAttentionCount)
        : LocalizationManager.Instance["OverviewGuidesAllDone"];

    public OverviewViewModel(
        ProgramsViewModel programsVm,
        WindowsUpdatesViewModel windowsUpdatesVm,
        DriversViewModel driversVm,
        PipPackagesViewModel pipPackagesVm,
        HardwareHubViewModel hardwareHubVm)
    {
        Programs = programsVm;
        WindowsUpdates = windowsUpdatesVm;
        Drivers = driversVm;
        PipPackages = pipPackagesVm;
        _hardwareHubVm = hardwareHubVm;

        Programs.Updates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProgramsSummary));
        WindowsUpdates.Updates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(WindowsUpdatesSummary));
        Drivers.Updates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DriversSummary));
        PipPackages.Updates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PipPackagesSummary));

        _hardwareHubVm.PropertyChanged += (_, e) =>
        {
            // Hardware is detected asynchronously after this VM is constructed; relay the
            // change so the cards on this page populate the moment detection finishes.
            if (e.PropertyName == nameof(HardwareHubViewModel.Hardware))
                OnPropertyChanged(nameof(Hardware));
            if (e.PropertyName == nameof(HardwareHubViewModel.GuidesNeedingAttentionCount))
                OnPropertyChanged(nameof(GuidesSummary));
        };

        // The summary strings above are plain properties, not indexer bindings, so a language
        // switch needs an explicit nudge to re-resolve them in the new language.
        LocalizationManager.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(ProgramsSummary));
            OnPropertyChanged(nameof(WindowsUpdatesSummary));
            OnPropertyChanged(nameof(DriversSummary));
            OnPropertyChanged(nameof(PipPackagesSummary));
            OnPropertyChanged(nameof(GuidesSummary));
        };
    }

    private static string FormatUpdateCount(int count) => count > 0
        ? string.Format(LocalizationManager.Instance["OverviewUpdatesFound"], count)
        : LocalizationManager.Instance["OverviewNoneFound"];
}
