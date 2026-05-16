using System.Windows;
using System.Windows.Input;

namespace XenUpdate.App.Views;

public partial class CrashReporterWindow : Window
{
    private readonly string _errorText;

    public CrashReporterWindow(Exception ex)
    {
        InitializeComponent();

        _errorText = $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        StackTraceBox.Text = _errorText;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CopyError_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_errorText))
        {
            Clipboard.SetText(_errorText);
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = global::System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                exePath = exePath[..^4] + ".exe";
            }

            global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // If restart fails, just exit.
        }

        Environment.Exit(1);
    }
}
