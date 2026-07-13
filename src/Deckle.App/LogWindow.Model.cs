// LogWindow — ring-buffer/filter engine and ILogWindowSink marshalling.

using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
    // ── ILogWindowSink (events from LogWindowSink) ─────────────────────────────

    public void Write(EventEntry entry)
    {
        // The listener emits on the EventSource origin thread; immediately
        // wrap as LogEntry to precompute Text once (same reasons as the legacy
        // pipeline: avoid repeated formatting during ListView virtualization).
        var le = new LogEntry(entry);
        if (DispatcherQueue.HasThreadAccess) AddEntrySafe(le);
        else
        {
            // Cross-thread (EventSource listener from a worker thread to UI).
            // Do NOT instrument this marshalling: observing the LogWindow
            // append reinjects MarshalQueued/Completed into LogWindow, which
            // re-appends, then re-observes: observer effect. The
            // _emittingMarshal guard limits recursion to ×3 instead of stack
            // overflow, but ×3 on the capture firehose is enough to drown the
            // window. TryEnqueueOrLog keeps the useful rejection warning
            // without the Verbose pair.
            DispatcherQueue.TryEnqueueOrLog(
                () => AddEntrySafe(le),
                "LOGWIN", "log entry");
        }
    }

    // Not exposed on the interface; ILogWindowSink is a write-only channel.
    // Kept as a public method for internal use (OnClearClick and future
    // programmatic reset). Same marshalling as Write.
    public void Clear()
    {
        if (DispatcherQueue.HasThreadAccess) ClearAll();
        else DispatcherQueue.TryEnqueueOrLog(ClearAll, "LOGWIN", "clear all");
    }

    // Beacon app icon (red = recording / grey = idle). Called from
    // TranscriptionEngine.StatusChanged via App.xaml.cs. Thread-safe.
    public void SetRecordingState(bool isRecording)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyRecordingState(isRecording);
        else
        {
            // Threading: real cross-thread site (engine worker thread through
            // StatusChanged to UI). Same pattern as Write.
            DispatcherQueue.TryEnqueueObserved(
                "ui-update", "log-window",
                () => ApplyRecordingState(isRecording),
                "LOGWIN", "recording state");
        }
    }

    private void ClearAll()
    {
        _entries.Clear();
        _visible.Clear();
        _itemsPanel = null;
    }

    // ── Implementation ─────────────────────────────────────────────────────────

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
            LogItems.ScrollIntoView(_visible[_visible.Count - 1]);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail($"scroll err: {ex.Message}");
        }
    }
}
