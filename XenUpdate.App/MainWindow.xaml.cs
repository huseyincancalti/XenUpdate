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

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && NavList.SelectedItem is ListBoxItem { Tag: AppPage page })
        {
            vm.NavigateToCommand.Execute(page);
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
}
