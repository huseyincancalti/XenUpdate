using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using XenUpdate.App.ViewModels;
using XenUpdate.Core.Models;

namespace XenUpdate.App.Views;

/// <summary>
/// Code-behind for the Settings page.
/// Hosts small selection-reading helpers for the blacklist DataGrid
/// (multi-select removal and clipboard copy), which cannot be expressed
/// cleanly as pure MVVM commands without a converter or behavior.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>Initializes the Settings view.</summary>
    public SettingsView() => InitializeComponent();

    /// <summary>
    /// Removes all currently selected blacklist rows.
    /// Reads <see cref="DataGrid.SelectedItems"/> so every Ctrl/Shift-selected
    /// row is included, then delegates to <see cref="SettingsViewModel.RemoveEntriesAsync"/>.
    /// </summary>
    private async void RemoveSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var selected = BlacklistDataGrid.SelectedItems
            .OfType<BlacklistEntry>()
            .ToList();

        if (selected.Count == 0)
        {
            return;
        }

        await viewModel.RemoveEntriesAsync(selected);
    }

    /// <summary>Copies the focused row's Package ID to the clipboard.</summary>
    private void CopyPackageIdMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlacklistDataGrid.SelectedItem is BlacklistEntry entry)
        {
            CopyToClipboard(entry.PackageId);
        }
    }

    /// <summary>Copies every selected row's Package ID to the clipboard, one per line.</summary>
    private void CopySelectedPackageIdsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var ids = BlacklistDataGrid.SelectedItems
            .OfType<BlacklistEntry>()
            .Select(entry => entry.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (ids.Count > 0)
        {
            CopyToClipboard(string.Join(Environment.NewLine, ids));
        }
    }

    private static void CopyToClipboard(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
        }
    }
}
