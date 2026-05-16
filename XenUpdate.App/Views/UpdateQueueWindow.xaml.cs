using System.Windows;
using System.Windows.Input;

namespace XenUpdate.App.Views;

public partial class UpdateQueueWindow : Window
{
    public UpdateQueueWindow()
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
}
