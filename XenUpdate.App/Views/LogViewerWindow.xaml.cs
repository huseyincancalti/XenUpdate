using System.Windows;
using System.Windows.Input;
using XenUpdate.App.ViewModels;
using XenUpdate.Core.Models;

namespace XenUpdate.App.Views;

public partial class LogViewerWindow : Window
{
    // Takes the ViewModel directly (rather than relying on Owner to cascade a DataContext,
    // which WPF does not do — Window.Owner is a z-order/lifetime relationship, not a visual-tree
    // one). Previously this window bound to the static AppLogger.Logs collection instead, which
    // only ever receives crash-handler entries — every normal scan/install log line goes through
    // ILoggerService/LogConsoleViewModel, a completely separate pipeline, so the window always
    // opened empty regardless of what had actually just happened.
    public LogViewerWindow(LogConsoleViewModel logConsole, AppSettings appSettings)
    {
        InitializeComponent();
        DataContext = logConsole;

        // The background layer reads SpotlightEnabled off AppSettings, not off LogConsoleViewModel
        // (the window's own DataContext), so it needs this set explicitly.
        BackgroundLayer.DataContext = appSettings;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void LogViewerWindow_OnPreviewMouseMove(object sender, MouseEventArgs e) =>
        BackgroundLayer.UpdateSpotlightPosition(e.GetPosition(BackgroundLayer));

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogConsoleViewModel logConsole || logConsole.Entries.Count == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, logConsole.Entries);
        Clipboard.SetText(text);
    }
}
