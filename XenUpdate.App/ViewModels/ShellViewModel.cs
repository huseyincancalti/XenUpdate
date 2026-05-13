using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// Represents the available navigation pages in the application.
/// </summary>
public enum AppPage
{
    Programs,
    WindowsUpdates,
    Drivers,
    Settings,
    HardwareHub
}

/// <summary>
/// The top-level ViewModel for the application shell (MainWindow).
/// Manages navigation between pages and owns the log console ViewModel.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    /// <summary>The ViewModel for the log console panel displayed at the bottom of the window.</summary>
    public LogConsoleViewModel LogConsole { get; }

    /// <summary>The currently displayed page ViewModel, bound to the ContentControl in MainWindow.</summary>
    [ObservableProperty]
    private ObservableObject? _currentPage;

    /// <summary>The currently active navigation selection.</summary>
    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Programs;

    // Page ViewModels are injected so they remain singletons across navigation.
    private readonly ProgramsViewModel _programsVm;
    private readonly WindowsUpdatesViewModel _windowsUpdatesVm;
    private readonly DriversViewModel _driversVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly HardwareHubViewModel _hardwareHubVm;

    /// <summary>
    /// Initializes the shell with all page ViewModels injected by the DI container.
    /// </summary>
    public ShellViewModel(
        ProgramsViewModel programsVm,
        WindowsUpdatesViewModel windowsUpdatesVm,
        DriversViewModel driversVm,
        SettingsViewModel settingsVm,
        HardwareHubViewModel hardwareHubVm,
        LogConsoleViewModel logConsole)
    {
        _programsVm = programsVm;
        _windowsUpdatesVm = windowsUpdatesVm;
        _driversVm = driversVm;
        _settingsVm = settingsVm;
        _hardwareHubVm = hardwareHubVm;
        LogConsole = logConsole;

        // Show Programs page on startup.
        NavigateTo(AppPage.Programs);
    }

    /// <summary>
    /// Switches the main content area to the given page.
    /// Called when the user clicks a navigation item.
    /// </summary>
    [RelayCommand]
    public void NavigateTo(AppPage page)
    {
        SelectedPage = page;
        CurrentPage = page switch
        {
            AppPage.Programs      => _programsVm,
            AppPage.WindowsUpdates => _windowsUpdatesVm,
            AppPage.Drivers       => _driversVm,
            AppPage.Settings      => _settingsVm,
            AppPage.HardwareHub   => _hardwareHubVm,
            _                     => _programsVm
        };
    }
}
