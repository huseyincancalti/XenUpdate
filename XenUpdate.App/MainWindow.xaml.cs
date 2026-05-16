using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.Messaging;
using MaterialDesignThemes.Wpf;
using XenUpdate.App.Messages;
using XenUpdate.App.ViewModels;
using XenUpdate.App.Views;
using XenUpdate.Core.Models;

namespace XenUpdate.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<UpdateSelectedMessage>(this, (_, _) =>
            Dispatcher.InvokeAsync(PlayAddToCartAnimation));

        WeakReferenceMessenger.Default.Register<NotificationMessage>(this, (_, m) =>
            Dispatcher.InvokeAsync(() => MainSnackbar.MessageQueue?.Enqueue(m.Message)));

        WeakReferenceMessenger.Default.Register<BalloonTipMessage>(this, (_, m) =>
            Dispatcher.InvokeAsync(() => AppTrayIcon.ShowNotification(m.Title, m.Message, H.NotifyIcon.Core.NotificationIcon.Info)));

        if (DataContext is MainViewModel vm)
        {
            BindViewModelActions(vm);
        }
        else
        {
            DataContextChanged += (_, e) =>
            {
                if (e.NewValue is MainViewModel newVm)
                {
                    BindViewModelActions(newVm);
                }
            };
        }
    }

    private void BindViewModelActions(MainViewModel vm)
    {
        vm.RequestShowWindow = () =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        vm.RequestCloseApp = () => System.Windows.Application.Current.Shutdown();
        vm.RequestOpenLogViewer = () =>
        {
            var logWin = new Views.LogViewerWindow { Owner = this };
            logWin.Show();
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ReviewInstall_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MoveToQueueCommand.Execute(null);
        }

        var win = new UpdateQueueWindow { DataContext = DataContext, Owner = this };
        win.Show();
    }

    private void OpenBlacklist_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _ = vm.OpenBlacklistManagerCommand.ExecuteAsync(null);
        }

        var win = new BlacklistWindow { DataContext = DataContext, Owner = this };
        win.Show();
    }

    private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnInteractiveChild(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is DataGridRow { DataContext: CategorizedUpdateItem item } row &&
            DataContext is MainViewModel vm)
        {
            vm.ToggleSelectionCommand.Execute(item);
            e.Handled = false;
        }
    }

    private static bool IsClickOnInteractiveChild(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is CheckBox or Button or ToggleButton)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void PlayAddToCartAnimation()
    {
        if (!IsLoaded || AnimationCanvas is null || ReviewButton is null)
        {
            return;
        }

        var startPoint = Mouse.GetPosition(AnimationCanvas);

        var btnBounds = ReviewButton.TransformToAncestor(AnimationCanvas)
            .TransformBounds(new Rect(ReviewButton.RenderSize));
        var endX = btnBounds.Left + btnBounds.Width / 2 - 7.5;
        var endY = btnBounds.Top + btnBounds.Height / 2 - 7.5;

        const double size = 15;
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xFF)),
            Opacity = 1,
            Effect = new DropShadowEffect
            {
                Color = Colors.Cyan,
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.9
            }
        };

        Canvas.SetLeft(dot, startPoint.X - size / 2);
        Canvas.SetTop(dot, startPoint.Y - size / 2);
        AnimationCanvas.Children.Add(dot);

        var duration = TimeSpan.FromSeconds(0.5);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var animX = new DoubleAnimation(startPoint.X - size / 2, endX, duration)
        {
            EasingFunction = ease
        };
        var animY = new DoubleAnimation(startPoint.Y - size / 2, endY, duration)
        {
            EasingFunction = ease
        };
        var animOpacity = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.15)))
        {
            BeginTime = TimeSpan.FromSeconds(0.38)
        };

        var sb = new Storyboard();
        Storyboard.SetTarget(animX, dot);
        Storyboard.SetTargetProperty(animX, new PropertyPath(Canvas.LeftProperty));
        Storyboard.SetTarget(animY, dot);
        Storyboard.SetTargetProperty(animY, new PropertyPath(Canvas.TopProperty));
        Storyboard.SetTarget(animOpacity, dot);
        Storyboard.SetTargetProperty(animOpacity, new PropertyPath(UIElement.OpacityProperty));

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Children.Add(animOpacity);
        sb.Completed += (_, _) => AnimationCanvas.Children.Remove(dot);
        sb.Begin();
    }
}
