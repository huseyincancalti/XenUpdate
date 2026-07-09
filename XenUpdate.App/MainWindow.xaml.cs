using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using XenUpdate.App.Messages;
using XenUpdate.App.ViewModels;

namespace XenUpdate.App;

public partial class MainWindow : Window
{
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();

        Activated += MainWindow_OnActivated;

        WeakReferenceMessenger.Default.Register<NotificationMessage>(this, (_, m) =>
            Dispatcher.InvokeAsync(() => MainSnackbar.MessageQueue?.Enqueue(m.Message)));

        WeakReferenceMessenger.Default.Register<BalloonTipMessage>(this, (_, m) =>
            Dispatcher.InvokeAsync(() => AppTrayIcon.ShowNotification(m.Title, m.Message, H.NotifyIcon.Core.NotificationIcon.Info)));

        if (DataContext is ShellViewModel vm)
        {
            BindViewModelActions(vm);
        }
        else
        {
            DataContextChanged += (_, e) =>
            {
                if (e.NewValue is ShellViewModel newVm)
                {
                    BindViewModelActions(newVm);
                }
            };
        }
    }

    private void BindViewModelActions(ShellViewModel vm)
    {
        vm.RequestShowWindow = () =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        vm.RequestCloseApp = () =>
        {
            _exitRequested = true;
            System.Windows.Application.Current.Shutdown();
        };
        vm.RequestOpenLogViewer = () =>
        {
            var logWin = new Views.LogViewerWindow { Owner = this };
            logWin.Show();
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Respect the user's choice: minimize-to-tray hides on close; otherwise actually exit.
        // An explicit tray "Exit" (_exitRequested) always closes.
        if (!_exitRequested)
        {
            var minimizeToTray = (DataContext as ShellViewModel)?.Settings.Settings.MinimizeToTray ?? false;
            if (minimizeToTray)
            {
                e.Cancel = true;
                Hide();
            }
        }

        base.OnClosing(e);
    }

    // The user may have just installed a driver update (or similar) in another window and
    // switched back — a natural point to quietly re-verify dynamic guide state instead of
    // waiting for the user to leave and re-enter the Guides page.
    private async void MainWindow_OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is ShellViewModel vm)
        {
            await vm.RefreshGuidesAfterActivationAsync();
        }
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && NavList.SelectedItem is ListBoxItem { Tag: AppPage page })
        {
            vm.NavigateToCommand.Execute(page);
        }
    }

    // Fires before the click bubbles up and the ListBox selects the parent "Guides" item, so
    // both the navigation (via NavList_SelectionChanged) and the specific-guide selection below
    // happen from one click on a sidebar sub-branch.
    private void GuideSidebarItem_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GuideSidebarItem item } && DataContext is ShellViewModel vm)
        {
            vm.GuidesViewModel.SelectGuideById(item.Id);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Route through Close() (not Hide()) so OnClosing's MinimizeToTray check always applies.
    // Calling Hide() directly used to bypass that check, silently orphaning the process in
    // the tray even when the user had "Minimize to tray on close" turned off.
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // Tray context menu items call through here (rather than binding Command in XAML) because
    // ContextMenu is a detached popup that does not reliably inherit TaskbarIcon's DataContext.
    private void OpenMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && vm.ShowWindowCommand.CanExecute(null))
        {
            vm.ShowWindowCommand.Execute(null);
        }
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && vm.ExitApplicationCommand.CanExecute(null))
        {
            vm.ExitApplicationCommand.Execute(null);
        }
    }
}
