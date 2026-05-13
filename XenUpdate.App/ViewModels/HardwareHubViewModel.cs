using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Hardware;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// ViewModel for the Hardware Hub page.
/// Aggregates all update providers, filters by <see cref="UpdateCategory"/>,
/// and exposes detected CPU/GPU info via <see cref="HardwareProfile"/>.
/// </summary>
public sealed partial class HardwareHubViewModel : ObservableObject
{
    // ── Observable collections ────────────────────────────────────────────────

    /// <summary>App (Winget) updates sourced from all providers.</summary>
    public ObservableCollection<CategorizedUpdateItem> AppUpdates { get; } = new();

    /// <summary>Windows OS updates sourced from all providers.</summary>
    public ObservableCollection<CategorizedUpdateItem> SystemUpdates { get; } = new();

    /// <summary>Driver updates sourced from all providers.</summary>
    public ObservableCollection<CategorizedUpdateItem> DriverUpdates { get; } = new();

    // ── Hardware profile ──────────────────────────────────────────────────────

    /// <summary>
    /// Detected GPU/CPU info resolved by <see cref="HardwareDetector"/> at construction time.
    /// </summary>
    [ObservableProperty]
    private HardwareProfile _hardware = new();

    // ── Loading state ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ── Construction ──────────────────────────────────────────────────────────

    private readonly IEnumerable<IUpdateProvider> _providers;

    /// <summary>
    /// Initialises the ViewModel. Hardware detection runs synchronously on a
    /// thread-pool thread so it never blocks the UI dispatcher.
    /// Call <see cref="InitializeAsync"/> from the view's Loaded event to pull
    /// update data from all providers.
    /// </summary>
    public HardwareHubViewModel(IEnumerable<IUpdateProvider> providers)
    {
        _providers = providers;

        // Run blocking WMI queries off the UI thread.
        Task.Run(() => Hardware = HardwareDetector.GetSystemHardware());
    }

    // ── Commands / init ───────────────────────────────────────────────────────

    /// <summary>
    /// Queries all registered <see cref="IUpdateProvider"/> instances and
    /// populates the three category collections.
    /// Safe to call multiple times (clears previous results on each call).
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading updates…";

        AppUpdates.Clear();
        SystemUpdates.Clear();
        DriverUpdates.Clear();

        try
        {
            foreach (var provider in _providers)
            {
                var items = await provider.GetUpdatesAsync().ConfigureAwait(true);

                foreach (var item in items)
                {
                    switch (item.Category)
                    {
                        case UpdateCategory.Apps:
                            AppUpdates.Add(item);
                            break;
                        case UpdateCategory.System:
                            SystemUpdates.Add(item);
                            break;
                        case UpdateCategory.Drivers:
                            DriverUpdates.Add(item);
                            break;
                        // HardwareHub items intentionally omitted from lists here;
                        // the Hardware tab shows static HardwareProfile info only.
                    }
                }
            }

            StatusMessage = $"Found {AppUpdates.Count + SystemUpdates.Count + DriverUpdates.Count} update(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading updates: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
