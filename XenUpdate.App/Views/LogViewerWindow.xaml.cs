using System.Windows;
using System.Windows.Input;
using XenUpdate.App.Services;

namespace XenUpdate.App.Views;

public partial class LogViewerWindow : Window
{
    public LogViewerWindow()
    {
        InitializeComponent();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var text = AppLogger.GetAll();
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Clear();
    }
}
