using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XenUpdate.Core.Models;

namespace XenUpdate.App.Views;

/// <summary>
/// Code-behind for the Python Packages page.
/// Hosts small row-selection and clipboard helpers for the context menu.
/// </summary>
public partial class PipPackagesView : UserControl
{
    private PipPackageItem? _contextItem;

    /// <summary>Initializes the Python Packages view.</summary>
    public PipPackagesView()
    {
        InitializeComponent();
    }

    private void PipPackagesDataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not PipPackageItem item)
        {
            _contextItem = null;
            return;
        }

        _contextItem = item;

        if (!row.IsSelected && Keyboard.Modifiers == ModifierKeys.None)
        {
            PipPackagesDataGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }

        PipPackagesDataGrid.CurrentItem = item;
        row.Focus();
    }

    private void PipPackagesContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        var hasContextItem = item is not null;
        var hasSelectedItems = GetSelectedItems().Count > 0;

        CopyPackageNameMenuItem.IsEnabled = hasContextItem;
        CopySelectedPackageNamesMenuItem.IsEnabled = hasSelectedItems;
    }

    private void CopyPackageNameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null)
        {
            return;
        }

        CopyToClipboard(item.PackageName);
    }

    private void CopySelectedPackageNamesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var names = GetSelectedItems()
            .Select(item => item.PackageName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (names.Count == 0)
        {
            return;
        }

        CopyToClipboard(string.Join(Environment.NewLine, names));
    }

    private PipPackageItem? GetContextItem()
    {
        return _contextItem ?? PipPackagesDataGrid.SelectedItem as PipPackageItem;
    }

    private List<PipPackageItem> GetSelectedItems()
    {
        return PipPackagesDataGrid.SelectedItems
            .OfType<PipPackageItem>()
            .ToList();
    }

    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Clipboard.SetText(text);
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
