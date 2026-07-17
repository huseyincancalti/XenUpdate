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
/// Code-behind for the Programs page.
/// Hosts small row-selection and clipboard helpers for the context menu.
/// </summary>
public partial class ProgramsView : UserControl
{
    private AppUpdateItem? _contextItem;
    private int? _lastClickedIndex;

    /// <summary>Initializes the Programs view.</summary>
    public ProgramsView()
    {
        InitializeComponent();
    }

    private void ProgramsDataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not AppUpdateItem item)
        {
            _contextItem = null;
            return;
        }

        _contextItem = item;

        if (!row.IsSelected && Keyboard.Modifiers == ModifierKeys.None)
        {
            ProgramsDataGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }

        ProgramsDataGrid.CurrentItem = item;
        row.Focus();
    }

    /// <summary>
    /// Toggles the clicked row's selection checkbox. The checkbox is hit-test invisible, so
    /// the entire row is the click target — clicking anywhere on a row checks/unchecks it.
    /// Row highlighting (used by the context menu) is left intact.
    /// Shift+click checks every row between the last-clicked row and this one (inclusive),
    /// matching the familiar file-explorer range-select gesture, without touching rows outside
    /// that range — a plain click still only ever affects the one row it lands on.
    /// </summary>
    private void ProgramsDataGrid_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not AppUpdateItem item
            || DataContext is not ProgramsViewModel viewModel)
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

    private void ProgramsContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        var contextItem = GetContextItem();
        var hasContextItem = contextItem is not null;
        var hasSelectedItems = GetSelectedItems().Count > 0;

        CopyErrorMessageMenuItem.IsEnabled = hasContextItem && contextItem?.Status == UpdateStatus.Failed;
        CopyPackageIdMenuItem.IsEnabled = hasContextItem;
        CopyAppNameMenuItem.IsEnabled = hasContextItem;
        CopyAppNameAndPackageIdMenuItem.IsEnabled = hasContextItem;
        AddToBlacklistMenuItem.IsEnabled = hasContextItem;
        AddToWhitelistMenuItem.IsEnabled = hasContextItem && contextItem?.IsWhitelisted == false;
        RemoveFromWhitelistMenuItem.IsEnabled = hasContextItem && contextItem?.IsWhitelisted == true;

        CopySelectedPackageIdsMenuItem.IsEnabled = hasSelectedItems;
        AddSelectedToBlacklistMenuItem.IsEnabled = hasSelectedItems;
        AddSelectedToWhitelistMenuItem.IsEnabled = hasSelectedItems;
        RemoveSelectedFromWhitelistMenuItem.IsEnabled = hasSelectedItems;
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

    private void CopyPackageIdMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null)
        {
            return;
        }

        CopyToClipboard(item.WingetPackageId);
    }

    private void CopyAppNameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null)
        {
            return;
        }

        CopyToClipboard(item.DisplayName);
    }

    private void CopyAppNameAndPackageIdMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null)
        {
            return;
        }

        CopyToClipboard($"{item.DisplayName} ({item.WingetPackageId})");
    }

    private void CopySelectedPackageIdsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var packageIds = GetSelectedItems()
            .Select(item => item.WingetPackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (packageIds.Count == 0)
        {
            return;
        }

        CopyToClipboard(string.Join(Environment.NewLine, packageIds));
    }

    private async void AddToBlacklistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || DataContext is not ProgramsViewModel viewModel)
        {
            return;
        }

        await viewModel.AddItemsToBlacklistAsync(new[] { item });
    }

    private async void AddSelectedToBlacklistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProgramsViewModel viewModel)
        {
            return;
        }

        var selectedItems = GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        await viewModel.AddItemsToBlacklistAsync(selectedItems);
    }

    private async void AddToWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || DataContext is not ProgramsViewModel viewModel)
        {
            return;
        }

        await viewModel.AddItemsToWhitelistAsync(new[] { item });
    }

    private async void RemoveFromWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || DataContext is not ProgramsViewModel viewModel)
        {
            return;
        }

        await viewModel.RemoveItemsFromWhitelistAsync(new[] { item });
    }

    private async void AddSelectedToWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProgramsViewModel viewModel)
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
        if (DataContext is not ProgramsViewModel viewModel)
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

    private AppUpdateItem? GetContextItem()
    {
        return _contextItem ?? ProgramsDataGrid.SelectedItem as AppUpdateItem;
    }

    private List<AppUpdateItem> GetSelectedItems()
    {
        return ProgramsDataGrid.SelectedItems
            .OfType<AppUpdateItem>()
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
