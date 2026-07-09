using System.Windows;
using System.Windows.Controls;
using XenUpdate.App.ViewModels;

namespace XenUpdate.App.Views;

/// <summary>
/// Code-behind for <see cref="HardwareHubView"/>.
/// Triggers async data loading once the view is rendered, and handles the
/// landing-card / back-link navigation between the guides list and a single guide's detail.
/// </summary>
public partial class HardwareHubView : UserControl
{
    public HardwareHubView()
    {
        InitializeComponent();
    }

    private async void HardwareHubView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is HardwareHubViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void GuideEntryCard_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GuideCardViewModel card } && DataContext is HardwareHubViewModel vm)
        {
            vm.SelectGuideById(card.Id);
        }
    }

    private void BackToLanding_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is HardwareHubViewModel vm)
        {
            vm.ShowLanding();
        }
    }
}
