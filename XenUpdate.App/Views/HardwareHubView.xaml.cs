using System.Windows.Controls;
using XenUpdate.App.ViewModels;

namespace XenUpdate.App.Views;

/// <summary>
/// Code-behind for <see cref="HardwareHubView"/>.
/// Triggers async data loading once the view is rendered.
/// </summary>
public partial class HardwareHubView : UserControl
{
    public HardwareHubView()
    {
        InitializeComponent();
    }

    private async void HardwareHubView_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is HardwareHubViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
