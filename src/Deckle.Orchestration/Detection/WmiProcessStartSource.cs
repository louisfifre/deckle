using System.Management;

namespace Deckle.Orchestration;

// Process start events from WMI's Win32_ProcessStartTrace: pushed by the
// system the moment a process starts, nothing polled. The subscription only
// succeeds when Deckle runs elevated ("Start elevated" on the Trackpad page); from a
// standard process WMI refuses it and Start() returns false.
//
// Start() and Stop() may run on any thread, never the UI one: connecting to
// WMI can take a moment. EventArrived and Stopped fire on WMI callback threads.
public sealed class WmiProcessStartSource : IProcessStartSource
{
    private const string Query = "SELECT * FROM Win32_ProcessStartTrace";

    // The refusal is the same for the whole process lifetime (elevation does
    // not change while Deckle runs), so its Warning is emitted once.
    private static int _unavailableReported;

    // A dropped event says the same thing each time (a WMI hiccup or a
    // faulty handler), so its Warning is emitted once per process lifetime.
    private static int _droppedReported;

    private readonly object _gate = new();
    private ManagementEventWatcher? _watcher;
    private bool _disposed;

    public event Action<ProcessStarted>? Started;

    public bool Start()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (_watcher is not null) return true;

            var watcher = new ManagementEventWatcher(new WqlEventQuery(Query));
            watcher.EventArrived += OnEventArrived;
            try
            {
                watcher.Start();
            }
            // Any failure here means the same thing to the runtime (no start
            // events), and an escape would fault the reconcile pass, so the
            // filter is not narrowed to the refusal types WMI documents.
            catch (Exception ex)
            {
                watcher.EventArrived -= OnEventArrived;
                watcher.Dispose();
                LogUnavailable(ex, firstTime: Interlocked.Exchange(ref _unavailableReported, 1) == 0);
                return false;
            }

            // Subscribed only once live, so a refused start never reports a loss.
            watcher.Stopped += OnStopped;
            Volatile.Write(ref _watcher, watcher);
            DeckleOrchestrationSource.Log.ProcessTraceStarted();
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            ManagementEventWatcher? watcher = _watcher;
            Volatile.Write(ref _watcher, null);
            if (watcher is null) return;

            // Unsubscribed before Stop, so stopping on purpose is not
            // reported as a loss.
            watcher.Stopped -= OnStopped;
            watcher.EventArrived -= OnEventArrived;
            try
            {
                watcher.Stop();
            }
            catch (Exception ex)
            {
                DeckleOrchestrationSource.Log.ProcessTraceStopFailed(ex.GetType().Name, ex.Message);
            }
            finally
            {
                watcher.Dispose();
            }
            DeckleOrchestrationSource.Log.ProcessTraceStopped();
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate) _disposed = true;
    }

    // Runs on a WMI thread: an exception escaping here would end the process,
    // so one event that cannot be read or handled is logged and dropped. The
    // two stages are told apart because a read failure is WMI's and a deliver
    // failure is a subscriber's.
    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        ProcessStarted started;
        try
        {
            ManagementBaseObject data = e.NewEvent;
            int pid = (int)Convert.ToUInt32(data["ProcessID"]);
            string name = AppMatcher.NormalizeProcessName(data["ProcessName"] as string);
            string? path = ProcessNativeMethods.TryGetImagePath(pid);
            started = new ProcessStarted(pid, name, path);
        }
        catch (Exception ex)
        {
            LogDropped("read", ex);
            return;
        }

        try
        {
            Started?.Invoke(started);
        }
        catch (Exception ex)
        {
            LogDropped("deliver", ex);
        }
    }

    // WMI ended the subscription on its own. Read without the gate: Stop
    // holds it while it waits on WMI, and this callback runs on a WMI thread.
    // Logged only; the runtime still counts the source as running, so it is
    // not restarted until the enabled entries drop to none and come back.
    private void OnStopped(object sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, Volatile.Read(ref _watcher))) return;
        DeckleOrchestrationSource.Log.ProcessTraceLost(e.Status.ToString());
    }

    private static void LogDropped(string stage, Exception ex)
    {
        if (Interlocked.Exchange(ref _droppedReported, 1) == 0)
        {
            DeckleOrchestrationSource.Log.ProcessTraceEventDropped(stage, ex.GetType().Name, ex.Message);
        }
        else
        {
            DeckleOrchestrationSource.Log.ProcessTraceEventDroppedDetail(stage, ex.GetType().Name, ex.Message);
        }
    }

    // Internal so the observability test checks what a refusal emits: the
    // Warning the first time, the Verbose detail on every later one.
    internal static void LogUnavailable(Exception ex, bool firstTime)
    {
        if (firstTime)
        {
            DeckleOrchestrationSource.Log.ProcessTraceUnavailable(
                Environment.IsPrivilegedProcess, ex.GetType().Name, ex.Message, ex.HResult);
        }
        else
        {
            DeckleOrchestrationSource.Log.ProcessTraceUnavailableDetail(ex.GetType().Name, ex.Message, ex.HResult);
        }
    }
}
