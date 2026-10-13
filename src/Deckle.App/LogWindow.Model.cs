// LogWindow — ring-buffer/filter engine and ILogWindowSink marshalling.

using System.Collections.Concurrent;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
    private const int EntryDrainBatchSize = 256;
    private readonly ConcurrentQueue<LogEntry> _pendingEntries = new();
    private int _entryDrainScheduled;

    public void Write(EventEntry entry)
    {
        // Called on the EventSource origin thread, so the formatting happens
        // here rather than on the UI thread.
        _pendingEntries.Enqueue(new LogEntry(entry));
        ScheduleEntryDrain();
    }

    private void ScheduleEntryDrain()
    {
        if (Interlocked.Exchange(ref _entryDrainScheduled, 1) != 0)
            return;

        bool enqueued = DispatcherQueue.TryEnqueueOrLog(
            DrainPendingEntries,
            "LOGWIN", "log entry batch");
        if (!enqueued)
            Volatile.Write(ref _entryDrainScheduled, 0);
    }

    private void DrainPendingEntries()
    {
        int drained = 0;
        while (drained < EntryDrainBatchSize && _pendingEntries.TryDequeue(out LogEntry? entry))
        {
            AddEntrySafe(entry);
            drained++;
        }

        Volatile.Write(ref _entryDrainScheduled, 0);
        if (!_pendingEntries.IsEmpty)
            ScheduleEntryDrain();
    }

    public void Clear()
    {
        if (DispatcherQueue.HasThreadAccess) ClearAll();
        else DispatcherQueue.TryEnqueueOrLog(ClearAll, "LOGWIN", "clear all");
    }

    public void SetRecordingState(bool isRecording)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyRecordingState(isRecording);
        else
        {
            // The engine raises StatusChanged on its worker thread.
            DispatcherQueue.TryEnqueueObserved(
                "ui-update", "log-window",
                () => ApplyRecordingState(isRecording),
                "LOGWIN", "recording state");
        }
    }

    private void ClearAll()
    {
        AppDiagnosticsBootstrap.ClearLogWindowHistory();
        while (_pendingEntries.TryDequeue(out _)) { }
        _entries.Clear();
        _visible.Clear();
    }

    private void AddEntrySafe(LogEntry entry)
    {
        FilterBar.Observe(entry.Entry);
        _entries.Enqueue(entry);

        const int MaxEntries = 5000;
        while (_entries.Count > MaxEntries)
        {
            var removed = _entries.Dequeue();
            // The visible projection preserves queue order. If the expired
            // entry is visible it can only be at index 0, so avoid the linear
            // IndexOf performed by ObservableCollection.Remove.
            if (_visible.Count > 0 && ReferenceEquals(_visible[0], removed))
                _visible.RemoveAt(0);
        }

        if (Matches(entry)) _visible.Add(entry);

        if (!_isVisible) return;
        if (AutoScrollToggle?.IsChecked != true) return;

        RequestScrollToBottom();
    }

    private bool Matches(LogEntry e)
    {
        if (!_filterSelection.Matches(e.Entry)) return false;

        if (_currentSearch.Length > 0 &&
            e.Text.IndexOf(_currentSearch, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    private void ApplyFilter()
    {
        _visible.ReplaceAll(_entries.Where(Matches));
        if (_isVisible && AutoScrollToggle?.IsChecked == true)
            RequestScrollToBottom();
    }

    private void RequestScrollToBottom()
    {
        if (_autoScrollPending) return;
        _autoScrollPending = true;

        bool enqueued = DispatcherQueue.TryEnqueueOrLog(
            () =>
            {
                _autoScrollPending = false;
                if (_isVisible && AutoScrollToggle?.IsChecked == true)
                    ScrollToBottom();
            },
            "LOGWIN", "auto scroll",
            DispatcherQueuePriority.Low);

        if (!enqueued) _autoScrollPending = false;
    }

    private void ScrollToBottom()
    {
        if (_visible.Count == 0) return;
        try
        {
            // ScrollIntoView(last entry) stops before ListView.Footer, so lay out
            // first, then move to the full extent including the tail.
            // disableAnimation keeps autoscroll independent of animation preferences.
            LogItems.UpdateLayout();
            ScrollViewer? viewer = GetListViewScrollViewer();
            viewer?.ChangeView(
                horizontalOffset: null,
                verticalOffset: viewer.ScrollableHeight,
                zoomFactor: null,
                disableAnimation: true);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail($"scroll err: {ex.Message}");
        }
    }
}
