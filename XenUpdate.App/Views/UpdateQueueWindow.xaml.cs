using System.Windows;
using System.Windows.Input;
using XenUpdate.App.ViewModels;
using XenUpdate.Core.Models;

namespace XenUpdate.App.Views;

public partial class UpdateQueueWindow : Window
{
    public UpdateQueueWindow(UpdateQueueViewModel updateQueue, AppSettings appSettings)
    {
        InitializeComponent();
        DataContext = updateQueue;

        // The background layer reads SpotlightEnabled off AppSettings, not off
        // UpdateQueueViewModel (the window's own DataContext), so it needs this set explicitly.
        BackgroundLayer.DataContext = appSettings;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void UpdateQueueWindow_OnPreviewMouseMove(object sender, MouseEventArgs e) =>
        BackgroundLayer.UpdateSpotlightPosition(e.GetPosition(BackgroundLayer));

    // Hides rather than closes: the window is a long-lived singleton view over
    // UpdateQueueViewModel (owned by ShellViewModel), reused for the next batch rather than
    // rebuilt each time.
    //
    // IMPORTANT: this must NOT be done via an OnClosing override that unconditionally cancels.
    // A previous version did exactly that, and it silently broke the app's exit path: when the
    // whole application shuts down (tray "Exit"), Application.Shutdown() closes every window in
    // Application.Windows as part of that sequence — including this one, even while hidden — and
    // WPF aborts the entire shutdown if any window's Closing gets canceled. That meant
    // App.OnExit (and the hard Environment.Exit(0) backstop in it) never ran at all, so the
    // process silently kept running until killed from Task Manager. The "Kapat" button calling
    // Hide() directly (bypassing Closing entirely) is what actually makes this window reusable;
    // real Close() calls (Alt+F4, or the app shutting down) are left alone and allowed to
    // proceed normally. MainWindow's Closed handler resets its cached reference to null so the
    // next RequestShow builds a fresh window instead of touching an already-closed one.
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
