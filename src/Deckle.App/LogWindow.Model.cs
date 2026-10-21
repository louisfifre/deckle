// LogWindow — bounded inbound queue, ring-buffer/filter engine and
// ILogWindowSink marshalling.

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
    // The display ring keeps LogWindowSink's replay depth. The inbound queue
    // adds one drain batch of headroom: the replay on first open fits whole
    // with the live entries that land before the first drain, so a notice
    // means the window really fell behind. An entry dropped beyond this depth
    // would have left the display ring anyway.
    private const int MaxEntries = 5000;
    private const int EntryDrainBatchSize = 256;
    private const int PendingCapacity = MaxEntries + EntryDrainBatchSize;
    private readonly ConcurrentQueue<LogEntry> _pendingEntries = new();
    private int _pendingCount;
    private int _droppedPendingCount;
    private int _entryDrainScheduled;

    public void Write(EventEntry entry)
    {
        // Called on the EventSource origin thread, so the formatting happens
        // here rather than on the UI thread.
        _pendingEntries.Enqueue(new LogEntry(entry));

        // Drop the oldest instead of BoundedWriteQueue's wait: an emitter on
        // the UI thread would block on a queue only the UI thread drains. The
        // count and the queue move apart for an instant under contention, so
        // the bound is approximate by a few entries, never open-ended.
        if (Interlocked.Increment(ref _pendingCount) > PendingCapacity
            && _pendingEntries.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _droppedPendingCount);
        }

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
            Interlocked.Decrement(ref _pendingCount);
            AddEntrySafe(entry);
            drained++;
        }

        int dropped = Interlocked.Exchange(ref _droppedPendingCount, 0);
        if (dropped > 0) ReportDroppedEntries(dropped);

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
        while (_pendingEntries.TryDequeue(out _))
            Interlocked.Decrement(ref _pendingCount);
        Interlocked.Exchange(ref _droppedPendingCount, 0);
        ResetDroppedEntries();
        _entries.Clear();
        _visible.Clear();
    }

    private void AddEntrySafe(LogEntry entry)
    {
        FilterBar.Observe(entry.Entry);
        _entries.Enqueue(entry);

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
            DeckleAppSource.Log.WindowOperationFailed(
                "log", "scroll_to_bottom", "exception", ex.GetType().Name, ex.Message);
        }
    }
}
