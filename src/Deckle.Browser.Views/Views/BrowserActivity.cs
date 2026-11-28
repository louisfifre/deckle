using System.Diagnostics;
using Deckle.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Deckle.Browser.Views;

/// <summary>The gestures the window logs, by name: a person's click, key or
/// drag on the panel or the page's card. The name is what the log carries.</summary>
public enum BrowserGesture
{
    AddressCommit,
    AddressCancel,
    Back,
    Forward,
    Reload,
    PaneToggle,
    NewTab,
    CloseTab,
    CloseTabMiddleClick,
    ReorderTab,
    PinTab,
    UnpinTab,
    SelectPinned,
    SelectTab,
    DownloadsOpen,
    SiteSettingsOpen,
    PageNotice,
    PermissionAnswer,
    PrivateWindow,
    AddressSuggestionChosen,
    FocusAddress,
    ReopenClosedTab,
    NextTab,
    PreviousTab,
    FindOpen,
    FindNext,
    FindPrevious,
    FindClose,
    MediaPlayPause,
    MediaPrevious,
    MediaNext,
    MediaMute,
    MediaPictureInPicture,
    MediaClose,
    MediaSelect,
}

// ─── What the window observes of itself ──────────────────────────────────────
//
// Two kinds of observation, as deckle-logging asks:
//   - a gesture and a top-level navigation are work with a start and an end,
//     each pair carrying one work id and the end its duration (GestureScope,
//     the navigation's pair in BrowserWindow);
//   - what pages do many times a second (requests, blocks, label, source and
//     icon changes) is only counted here, and a summary event carries the counts
//     every 10 s while any is not zero, and once more as a window closes.
//
// The counters also hold the UI thread's stalls: a timer ticks every 100 ms
// on the window's own dispatcher, and a tick that comes more than 200 ms late
// is a stall. The summary carries their count and the worst lateness.
//
// Numbers and names of kinds only: no address, no host, no title.
internal static class BrowserActivity
{
    private const int TickMs = 100;
    private const int StallAfterLateMs = 200;
    private const int SummaryEveryMs = 10_000;

    private static readonly object Gate = new();
    private static long _requests, _blocked, _labels, _sources, _icons, _navigations, _stalls, _worstStallMs;
    private static long _suggestions, _findSearches, _mediaReads, _requestsTotal;
    private static long _stallsTotal, _worstStallTotalMs;
    private static long _sinceTicks = Stopwatch.GetTimestamp();
    private static long _lastTickTicks;
    private static DispatcherQueueTimer? _tick, _summary;

    public static void Request()
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Increment(ref _requestsTotal);
    }

    /// <summary>Every request the pages made since the process began (the summary's count restarts, this does not).</summary>
    public static long RequestsTotal => Interlocked.Read(ref _requestsTotal);
    public static void Blocked() => Interlocked.Increment(ref _blocked);
    public static void LabelChanged() => Interlocked.Increment(ref _labels);
    public static void SourceChanged() => Interlocked.Increment(ref _sources);
    public static void IconChanged() => Interlocked.Increment(ref _icons);
    public static void NavigationBegan() => Interlocked.Increment(ref _navigations);

    // The comfort's bursts: a list of suggestions made per keystroke in the
    // address field, a find started per keystroke in the find bar, a read of the
    // playing page's media each second while the card shows.
    public static void SuggestionsMade() => Interlocked.Increment(ref _suggestions);
    public static void FindSearched() => Interlocked.Increment(ref _findSearches);
    public static void MediaRead() => Interlocked.Increment(ref _mediaReads);

    /// <summary>The UI thread's stalls since the process began: how many ticks
    /// came more than 200 ms late, and the worst lateness in milliseconds.</summary>
    public static (long Count, long WorstMs) Stalls
    {
        get { lock (Gate) return (_stallsTotal, _worstStallTotalMs); }
    }

    /// <summary>Starts the watch and the summary on a window's dispatcher, once
    /// for the process: the browser's windows share the UI thread.</summary>
    public static void Start(DispatcherQueue queue)
    {
        lock (Gate)
        {
            if (_tick is not null) return;
            _lastTickTicks = Stopwatch.GetTimestamp();
            _tick = queue.CreateTimer();
            _tick.Interval = TimeSpan.FromMilliseconds(TickMs);
            _tick.IsRepeating = true;
            _tick.Tick += (_, _) => OnTick();
            _tick.Start();

            _summary = queue.CreateTimer();
            _summary.Interval = TimeSpan.FromMilliseconds(SummaryEveryMs);
            _summary.IsRepeating = true;
            _summary.Tick += (_, _) => Flush();
            _summary.Start();
        }
    }

    private static void OnTick()
    {
        long now = Stopwatch.GetTimestamp();
        long elapsedMs = (now - _lastTickTicks) * 1000 / Stopwatch.Frequency;
        _lastTickTicks = now;
        long lateMs = elapsedMs - TickMs;
        if (lateMs <= StallAfterLateMs) return;

        lock (Gate)
        {
            _stalls++;
            _stallsTotal++;
            _worstStallMs = Math.Max(_worstStallMs, lateMs);
            _worstStallTotalMs = Math.Max(_worstStallTotalMs, lateMs);
        }
    }

    /// <summary>Writes the summary of what was counted since the last one, if
    /// anything was, and starts counting again.</summary>
    public static void Flush()
    {
        long requests = Interlocked.Exchange(ref _requests, 0);
        long blocked = Interlocked.Exchange(ref _blocked, 0);
        long labels = Interlocked.Exchange(ref _labels, 0);
        long sources = Interlocked.Exchange(ref _sources, 0);
        long icons = Interlocked.Exchange(ref _icons, 0);
        long navigations = Interlocked.Exchange(ref _navigations, 0);
        long suggestions = Interlocked.Exchange(ref _suggestions, 0);
        long findSearches = Interlocked.Exchange(ref _findSearches, 0);
        long mediaReads = Interlocked.Exchange(ref _mediaReads, 0);
        long stalls, worst;
        lock (Gate)
        {
            stalls = _stalls;
            worst = _worstStallMs;
            _stalls = 0;
            _worstStallMs = 0;
        }

        long now = Stopwatch.GetTimestamp();
        long intervalMs = (now - _sinceTicks) * 1000 / Stopwatch.Frequency;
        _sinceTicks = now;
        if (requests + blocked + labels + sources + icons + navigations + suggestions + findSearches + mediaReads + stalls == 0) return;

        DeckleBrowserViewsSource.Log.ActivitySummary(
            intervalMs, requests, blocked, labels, sources, icons, navigations, suggestions, findSearches, mediaReads, stalls, worst);
    }
}

/// <summary>One gesture, from its start to its end in the log: the same work
/// id on both, the duration on the end. Disposed when the window has done
/// what the gesture asked; an outcome other than "done" says it did less.</summary>
internal sealed class GestureScope : IDisposable
{
    private readonly BrowserGesture _kind;
    private readonly string _workId = WorkId.Next();
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public GestureScope(BrowserGesture kind)
    {
        _kind = kind;
        DeckleBrowserViewsSource.Log.GestureStarted(kind.ToString(), _workId);
    }

    /// <summary>"done" unless the gesture changed nothing ("refused").</summary>
    public string Outcome { get; set; } = "done";

    public void Dispose() =>
        DeckleBrowserViewsSource.Log.GestureEnded(
            _kind.ToString(), Outcome, Stopwatch.GetElapsedTime(_startedAt).Ticks / TimeSpan.TicksPerMillisecond, _workId);
}
