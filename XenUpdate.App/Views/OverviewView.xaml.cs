using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XenUpdate.App.ViewModels;

namespace XenUpdate.App.Views;

/// <summary>
/// Code-behind for <see cref="OverviewView"/>. The quick-stat cards carry the target
/// <see cref="AppPage"/> in their Tag and navigate through the shell (the window's
/// DataContext), the same pattern used for the sidebar's guide sub-branches.
/// </summary>
public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }

    private void StatCard_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AppPage page })
            return;

        if (Window.GetWindow(this)?.DataContext is ShellViewModel shell)
        {
            shell.NavigateToCommand.Execute(page);
        }
    }
}
