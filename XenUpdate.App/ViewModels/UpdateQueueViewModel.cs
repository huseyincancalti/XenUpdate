using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GongSolutions.Wpf.DragDrop;
using XenUpdate.App.Messages;
using XenUpdate.App.Services;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// Backs the update queue window: a live view of whatever install batch (or sequence of
/// batches, across pages, as "Update All" runs each page in turn) is currently running or just
/// finished. This VM never runs installs itself — each page's own install loop is already
/// correct and does that; this only observes the same <see cref="UpdateItem"/> instances that
/// loop is already mutating. Registered as a DI singleton so it persists across the whole app
/// session and is shared between ShellViewModel and the (lazily shown) window.
///
/// The queue is ONE flat list (<see cref="Entries"/>), not per-page groups — the user asked for
/// exactly that: no "Python Packages" section headers carving the list up, just each row
/// carrying a faint source tag, and free drag-reordering across the whole queue (a Python
/// package can be dragged above a winget app). ShellViewModel's Update All loop reads this
/// list's live order before every single item it installs, so a reorder here genuinely changes
/// execution order, Steam-download-queue-style.
/// </summary>
public sealed partial class UpdateQueueViewModel : ObservableObject
{
    private readonly List<UpdateItem> _trackedItems = new();

    public ObservableCollection<UpdateQueueEntry> Entries { get; } = new();

    /// <summary>Bound to the queue ItemsControl's dd:DragDrop.DropHandler — see UpdateQueueEntryDropHandler for the Pending-only reorder rule.</summary>
    public IDropTarget QueueDropHandler { get; } = new UpdateQueueEntryDropHandler();

    /// <summary>Set by MainWindow so this VM can pop the queue window into view without owning a Window reference itself.</summary>
    public Action? RequestShow { get; set; }

    /// <summary>Set by MainWindow so "View Log" opens the same log viewer the rest of the app uses.</summary>
    public Action? RequestOpenLog { get; set; }

    public int TotalCount => Entries.Count;

    public int CompletedCount => Entries.Count(e => e.Item.Status is UpdateStatus.Succeeded or UpdateStatus.Failed);

    public int SucceededCount => Entries.Count(e => e.Item.Status == UpdateStatus.Succeeded);

    public int FailedCount => Entries.Count(e => e.Item.Status == UpdateStatus.Failed);

    /// <summary>True once every tracked item has finished (Succeeded or Failed) — drives the footer's idle/running title and gates the Clear command.</summary>
    public bool IsComplete => TotalCount > 0 && CompletedCount == TotalCount;

    public bool HasFailures => FailedCount > 0;

    /// <summary>
    /// A full, localized "3 of 7 complete" sentence — computed here rather than concatenated
    /// from separate XAML Runs, since languages like Turkish can't express that as a simple
    /// "{count} of {total} complete" word order.
    /// </summary>
    public string ProgressText => string.Format(LocalizationManager.Instance["UpdateQueueProgress"], CompletedCount, TotalCount);

    public UpdateQueueViewModel()
    {
        WeakReferenceMessenger.Default.Register<InstallBatchStartedMessage>(this, (_, message) => OnBatchStarted(message));
    }

    /// <summary>
    /// Announces every page's batch for an "Update All" run in one shot, before any of them
    /// actually start installing — so the window shows the complete plan immediately (which
    /// items, from which sources) instead of only discovering the next page's items as a
    /// surprise once the previous one finishes.
    /// </summary>
    public void AnnouncePlan(IReadOnlyList<(string Label, IReadOnlyList<UpdateItem> Items)> batches)
    {
        foreach (var (label, items) in batches)
        {
            Announce(label, items);
        }

        RequestShow?.Invoke();
    }

    private void OnBatchStarted(InstallBatchStartedMessage message)
    {
        Announce(message.SourceLabel, message.Items);
        RequestShow?.Invoke();
    }

    /// <summary>
    /// Adds one source's batch to the flat queue. Any earlier entries for the same items or the
    /// same source are removed first (re-running a page shouldn't leave duplicate rows), but the
    /// rest of the board is never auto-cleared — Steam's download queue doesn't clear itself
    /// either, it keeps every entry until the user dismisses it (see ClearCommand) — so a
    /// multi-source run accumulates everything together instead of only ever showing whichever
    /// source happens to be running right now.
    /// </summary>
    private void Announce(string label, IReadOnlyList<UpdateItem> items)
    {
        var stale = Entries.Where(e => e.SourceLabel == label || items.Contains(e.Item)).ToList();
        foreach (var entry in stale)
        {
            entry.Item.PropertyChanged -= OnTrackedItemPropertyChanged;
            _trackedItems.Remove(entry.Item);
            Entries.Remove(entry);
        }

        foreach (var item in items)
        {
            Entries.Add(new UpdateQueueEntry(item, label));
            item.PropertyChanged += OnTrackedItemPropertyChanged;
            _trackedItems.Add(item);
        }

        RaiseAggregatesChanged();
    }

    private void OnTrackedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateItem.Status))
        {
            RaiseAggregatesChanged();
        }
    }

    /// <summary>Manually resets the board — the only way it ever clears now that arriving batches always append/replace-in-place. Only meaningful once everything tracked has finished.</summary>
    [RelayCommand(CanExecute = nameof(IsComplete))]
    private void Clear()
    {
        foreach (var item in _trackedItems)
        {
            item.PropertyChanged -= OnTrackedItemPropertyChanged;
        }

        _trackedItems.Clear();
        Entries.Clear();
        RaiseAggregatesChanged();
    }

    private void RaiseAggregatesChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(SucceededCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(ProgressText));
        ClearCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ViewLog() => RequestOpenLog?.Invoke();
}

/// <summary>
/// One row of the flat update queue: the exact item instance the source page is mutating, plus
/// which page it came from — shown only as a faint inline tag next to the name, not as a
/// section header (the user explicitly rejected source-grouped sections).
/// </summary>
public sealed class UpdateQueueEntry
{
    public UpdateQueueEntry(UpdateItem item, string sourceLabel)
    {
        Item = item;
        SourceLabel = sourceLabel;
    }

    public UpdateItem Item { get; }

    public string SourceLabel { get; }
}
