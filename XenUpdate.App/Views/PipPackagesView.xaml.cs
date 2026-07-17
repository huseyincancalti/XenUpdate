using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XenUpdate.App.ViewModels;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Models;

namespace XenUpdate.App.Views;

/// <summary>
/// Code-behind for the Python Packages page.
/// Hosts small row-selection and clipboard helpers for the context menu.
/// </summary>
public partial class PipPackagesView : UserControl
{
    private PipPackageItem? _contextItem;
    private int? _lastClickedIndex;

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

    /// <summary>
    /// Toggles the clicked row's selection checkbox. The checkbox is hit-test invisible, so
    /// the entire row is the click target — clicking anywhere on a row checks/unchecks it.
    /// Row highlighting (used by the context menu) is left intact. This page was previously
    /// missing this handler entirely — its checkbox is hit-test invisible like Programs', but
    /// without a row handler to compensate, nothing here was clickable at all.
    /// Shift+click checks every row between the last-clicked row and this one (inclusive),
    /// matching the familiar file-explorer range-select gesture, without touching rows outside
    /// that range — a plain click still only ever affects the one row it lands on.
    /// </summary>
    private void PipPackagesDataGrid_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not PipPackageItem item
            || DataContext is not PipPackagesViewModel viewModel)
        {
            return;
        }

        var items = viewModel.Updates;
        var clickedIndex = items.IndexOf(item);

        if (Keyboard.Modifiers == ModifierKeys.Shift && _lastClickedIndex is int anchor && clickedIndex >= 0)
        {
            var start = Math.Min(anchor, clickedIndex);
            var end = Math.Max(anchor, clickedIndex);
            for (var i = start; i <= end; i++)
            {
                items[i].IsSelected = true;
            }
        }
        else
        {
            item.IsSelected = !item.IsSelected;
        }

        _lastClickedIndex = clickedIndex;
    }

    private void PipPackagesContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        var hasContextItem = item is not null;
        var hasSelectedItems = GetSelectedItems().Count > 0;

        CopyErrorMessageMenuItem.IsEnabled = hasContextItem && item?.Status == UpdateStatus.Failed;
        CopyPackageNameMenuItem.IsEnabled = hasContextItem;
        CopySelectedPackageNamesMenuItem.IsEnabled = hasSelectedItems;
        AddToWhitelistMenuItem.IsEnabled = hasContextItem && item?.IsWhitelisted == false;
        RemoveFromWhitelistMenuItem.IsEnabled = hasContextItem && item?.IsWhitelisted == true;
        AddSelectedToWhitelistMenuItem.IsEnabled = hasSelectedItems;
        RemoveSelectedFromWhitelistMenuItem.IsEnabled = hasSelectedItems;
    }

    private async void AddToWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || DataContext is not PipPackagesViewModel viewModel)
        {
            return;
        }

        await viewModel.AddItemsToWhitelistAsync(new[] { item });
    }

    private async void RemoveFromWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || DataContext is not PipPackagesViewModel viewModel)
        {
            return;
        }

        await viewModel.RemoveItemsFromWhitelistAsync(new[] { item });
    }

    private async void AddSelectedToWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PipPackagesViewModel viewModel)
        {
            return;
        }

        var selectedItems = GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        await viewModel.AddItemsToWhitelistAsync(selectedItems);
    }

    private async void RemoveSelectedFromWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PipPackagesViewModel viewModel)
        {
            return;
        }

        var selectedItems = GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        await viewModel.RemoveItemsFromWhitelistAsync(selectedItems);
    }

    private void CopyErrorMessageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item?.ErrorMessage is null)
        {
            return;
        }

        CopyToClipboard(item.ErrorMessage);
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
