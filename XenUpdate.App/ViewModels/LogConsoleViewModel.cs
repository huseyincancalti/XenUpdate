using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XenUpdate.App.Collections;
using XenUpdate.Core.Enums;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;

namespace XenUpdate.App.ViewModels;

/// <summary>
/// ViewModel for the log console drawer displayed at the bottom of the main window.
/// Subscribes to <see cref="ILoggerService.LogEntryAdded"/> and appends entries
/// to a UI-bound collection.
///
/// File logging happens immediately inside the logger service; the UI side buffers
/// incoming entries and flushes them on a short <see cref="DispatcherTimer"/> tick so
/// bursts of log messages never thrash the UI thread.
/// </summary>
public sealed partial class LogConsoleViewModel : ObservableObject
{
    private const int MaxEntries = 200;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);

    private readonly ILoggerService _logger;
    private readonly ConcurrentQueue<LogEntry> _pendingEntries = new();
    private readonly DispatcherTimer _flushTimer;

    /// <summary>
    /// The collection of recent log entries displayed in the UI log panel.
    /// Bound to the ListView in the log drawer.
    /// </summary>
    public BulkObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>
    /// Whether the log drawer is currently expanded. Remembered for the current app session.
    /// Default is collapsed so the log panel doesn't take permanent screen space.
    /// </summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// How many error-level entries are currently in the list.
    /// Drives the small error badge shown on the drawer header when the drawer is collapsed.
    /// </summary>
    [ObservableProperty]
    private int _errorCount;

    /// <summary>True when at least one error-level entry is present.</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>
    /// Initializes the LogConsoleViewModel, subscribes to logger events, and starts
    /// the batched UI flush timer.
    /// </summary>
    public LogConsoleViewModel(ILoggerService logger)
    {
        _logger = logger;

        // Subscribe to the logger's event. Callback may come from a background thread;
        // we enqueue only and let the timer flush on the UI thread.
        _logger.LogEntryAdded += OnLogEntryAdded;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FlushInterval
        };
        _flushTimer.Tick += OnFlushTick;
        _flushTimer.Start();
    }

    /// <summary>Toggles the drawer open/closed state.</summary>
    [RelayCommand]
    private void ToggleOpen()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>Removes every entry and resets the error indicator.</summary>
    [RelayCommand]
    private void ClearEntries()
    {
        Entries.Clear();
        ErrorCount = 0;
    }

    partial void OnErrorCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>
    /// Queues the entry for the next UI flush. Runs on whichever thread the logger used.
    /// </summary>
    private void OnLogEntryAdded(LogEntry entry)
    {
        _pendingEntries.Enqueue(entry);
    }

    /// <summary>
    /// Drains the pending queue and merges new entries into the visible list.
    /// Always runs on the UI thread because DispatcherTimer raises Tick there.
    /// </summary>
    private void OnFlushTick(object? sender, EventArgs e)
    {
        if (_pendingEntries.IsEmpty)
        {
            return;
        }

        var sawError = false;
        var newErrors = 0;

        // Drain the queue, preserving arrival order.
        while (_pendingEntries.TryDequeue(out var entry))
        {
            // Insert at 0 so newest is at the top. For typical burst sizes this is fine;
            // virtualizing ListView keeps the visual work cheap.
            Entries.Insert(0, entry);

            if (entry.Severity == LogSeverity.Error)
            {
                newErrors++;
                sawError = true;
            }
        }

        // Trim to the max size in one pass, adjusting error count for trimmed errors too.
        var removedErrors = 0;
        while (Entries.Count > MaxEntries)
        {
            var last = Entries[Entries.Count - 1];
            if (last.Severity == LogSeverity.Error)
            {
                removedErrors++;
            }
            Entries.RemoveAt(Entries.Count - 1);
        }

        if (newErrors != 0 || removedErrors != 0)
        {
            ErrorCount = Math.Max(0, ErrorCount + newErrors - removedErrors);
        }

        // Auto-open the drawer on new errors so the user notices them immediately.
        if (sawError)
        {
            IsOpen = true;
        }
    }
}
