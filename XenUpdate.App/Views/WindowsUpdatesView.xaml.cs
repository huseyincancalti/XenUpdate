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
/// Code-behind for the Windows Updates page.
/// Hosts small row-selection and clipboard helpers for the context menu.
/// </summary>
public partial class WindowsUpdatesView : UserControl
{
    private WindowsUpdateItem? _contextItem;
    private int? _lastClickedIndex;

    /// <summary>Initializes the Windows Updates view.</summary>
    public WindowsUpdatesView()
    {
        InitializeComponent();
    }

    private void WindowsUpdatesDataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not WindowsUpdateItem item)
        {
            _contextItem = null;
            return;
        }

        _contextItem = item;

        if (!row.IsSelected && Keyboard.Modifiers == ModifierKeys.None)
        {
            WindowsUpdatesDataGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }

        WindowsUpdatesDataGrid.CurrentItem = item;
        row.Focus();
    }

    /// <summary>
    /// Toggles the clicked row's selection checkbox. The checkbox is hit-test invisible, so
    /// the entire row is the click target — clicking anywhere on a row checks/unchecks it,
    /// matching Programs/Python Packages.
    /// Shift+click checks every row between the last-clicked row and this one (inclusive),
    /// matching the familiar file-explorer range-select gesture, without touching rows outside
    /// that range — a plain click still only ever affects the one row it lands on.
    /// </summary>
    private void WindowsUpdatesDataGrid_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not WindowsUpdateItem item
            || DataContext is not WindowsUpdatesViewModel viewModel)
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

    private void WindowsUpdatesContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        var hasContextItem = item is not null;
        var hasKbArticle = !string.IsNullOrWhiteSpace(item?.KbArticleId);
        var hasSelectedItems = GetSelectedItems().Count > 0;

        CopyErrorMessageMenuItem.IsEnabled = hasContextItem && item?.Status == UpdateStatus.Failed;
        CopyUpdateTitleMenuItem.IsEnabled = hasContextItem;
        CopyKbArticleMenuItem.IsEnabled = hasContextItem && hasKbArticle;
        CopyUpdateTitleAndKbMenuItem.IsEnabled = hasContextItem && hasKbArticle;
        CopySelectedUpdateTitlesMenuItem.IsEnabled = hasSelectedItems;
        AddToWhitelistMenuItem.IsEnabled = hasContextItem && item?.IsWhitelisted == false;
        RemoveFromWhitelistMenuItem.IsEnabled = hasContextItem && item?.IsWhitelisted == true;
        AddSelectedToWhitelistMenuItem.IsEnabled = hasSelectedItems;
        RemoveSelectedFromWhitelistMenuItem.IsEnabled = hasSelectedItems;
    }

    private async void AddToWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || DataContext is not WindowsUpdatesViewModel viewModel)
        {
            return;
        }

        await viewModel.AddItemsToWhitelistAsync(new[] { item });
    }

    private async void RemoveFromWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || DataContext is not WindowsUpdatesViewModel viewModel)
        {
            return;
        }

        await viewModel.RemoveItemsFromWhitelistAsync(new[] { item });
    }

    private async void AddSelectedToWhitelistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WindowsUpdatesViewModel viewModel)
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
        if (DataContext is not WindowsUpdatesViewModel viewModel)
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

    private void CopyUpdateTitleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null)
        {
            return;
        }

        CopyToClipboard(item.DisplayName);
    }

    private void CopyKbArticleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null)
        {
            return;
        }

        CopyToClipboard(item.KbArticleId);
    }

    private void CopyUpdateTitleAndKbMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem();
        if (item is null || string.IsNullOrWhiteSpace(item.KbArticleId))
        {
            return;
        }

        CopyToClipboard($"{item.DisplayName} ({item.KbArticleId})");
    }

    private void CopySelectedUpdateTitlesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var titles = GetSelectedItems()
            .Select(item => item.DisplayName)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToList();

        if (titles.Count == 0)
        {
            return;
        }

        CopyToClipboard(string.Join(Environment.NewLine, titles));
    }

    private WindowsUpdateItem? GetContextItem()
    {
        return _contextItem ?? WindowsUpdatesDataGrid.SelectedItem as WindowsUpdateItem;
    }

    private List<WindowsUpdateItem> GetSelectedItems()
    {
        return WindowsUpdatesDataGrid.SelectedItems
            .OfType<WindowsUpdateItem>()
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
