// LogWindow — bounded inbound queue, ring-buffer/filter engine and
// ILogWindowSink marshalling.

using System.Collections.Concurrent;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Localization;
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
        if (drained > 0) UpdateDetailNotice();

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
        _hiddenDetailCount = 0;
        UpdateDetailNotice();
    }

    // Entries in the buffer that the detail switch alone keeps out of view.
    // Kept by AddEntrySafe and recounted by ApplyFilter, which every change of
    // filter or search goes through.
    private int _hiddenDetailCount;

    private bool IsHiddenDetail(LogEntry e)
        => _filterSelection.IsHiddenDetail(e.Entry) && MatchesSearch(e);

    // While the detail is hidden the filter row says so, with how many lines
    // it keeps out of view, so a quiet window never reads as a quiet app.
    private void UpdateDetailNotice()
    {
        if (_filterSelection.ShowDetail)
        {
            FilterBar.Notice = null;
            return;
        }
        // The count can lag a search typed but not yet applied; never below 0.
        int hidden = Math.Max(0, _hiddenDetailCount);
        FilterBar.Notice = hidden == 0
            ? Loc.Get("LogWindow_DetailHidden")
            : Loc.Format("LogWindow_DetailHidden_Format", hidden);
    }

    private void AddEntrySafe(LogEntry entry)
    {
        FilterBar.Observe(entry.Entry);
        _entries.Enqueue(entry);

        while (_entries.Count > MaxEntries)
        {
            var removed = _entries.Dequeue();
            if (IsHiddenDetail(removed)) _hiddenDetailCount--;
            // The visible projection preserves queue order. If the expired
            // entry is visible it can only be at index 0, so avoid the linear
            // IndexOf performed by ObservableCollection.Remove.
            if (_visible.Count > 0 && ReferenceEquals(_visible[0], removed))
                _visible.RemoveAt(0);
        }

        if (Matches(entry)) _visible.Add(entry);
        else if (IsHiddenDetail(entry)) _hiddenDetailCount++;

        if (!_isVisible) return;
        if (AutoScrollToggle?.IsChecked != true) return;

        RequestScrollToBottom();
    }

    private bool Matches(LogEntry e)
    {
        return _filterSelection.Matches(e.Entry) && MatchesSearch(e);
    }

    private bool MatchesSearch(LogEntry e)
        => _currentSearch.Length == 0
        || e.Text.IndexOf(_currentSearch, StringComparison.OrdinalIgnoreCase) >= 0;

    private void ApplyFilter()
    {
        _visible.ReplaceAll(_entries.Where(Matches));
        _hiddenDetailCount = _filterSelection.ShowDetail ? 0 : _entries.Count(IsHiddenDetail);
        UpdateDetailNotice();
        UpdateCopyCommand();
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

    // The self-check's second image of this window, at its last row, whether
    // or not the window counts as visible to the person.
    internal void ScrollToBottomForCapture() => ScrollToBottom();

    private void ScrollToBottom()
    {
        if (_visible.Count == 0) return;
        try
        {
            // The list is virtualized: it builds only the rows near the view and
            // estimates the height of the others from the rows it has built. A
            // Warning or Error row is two lines high, so that estimate runs short
            // and ScrollableHeight read before the last rows exist stopped the view
            // rows before the end, its bottom row cut by the edge. ScrollIntoView
            // builds the last rows, which makes the extent at the end exact; it
            // stops before ListView.Footer, so the view then moves to the full
            // extent, tail included. disableAnimation keeps autoscroll independent
            // of animation preferences.
            LogItems.ScrollIntoView(_visible[^1], ScrollIntoViewAlignment.Leading);
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
