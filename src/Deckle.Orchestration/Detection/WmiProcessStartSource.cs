using System.Management;
using System.Runtime.InteropServices;

namespace Deckle.Orchestration;

// Process start events from WMI's Win32_ProcessStartTrace: pushed by the
// system the moment a process starts, nothing polled. The subscription only
// succeeds when Deckle runs elevated ("Start elevated" on the Trackpad page); from a
// standard process WMI refuses it and Start() returns false.
//
// Start() and Stop() may run on any thread, never the UI one: connecting to
// WMI can take a moment. EventArrived fires on a WMI callback thread.
public sealed class WmiProcessStartSource : IProcessStartSource
{
    private const string Query = "SELECT * FROM Win32_ProcessStartTrace";

    // The refusal is the same for the whole process lifetime (elevation does
    // not change while Deckle runs), so its Warning is emitted once.
    private static int _unavailableReported;

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
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            {
                watcher.EventArrived -= OnEventArrived;
                watcher.Dispose();
                LogUnavailable(ex, firstTime: Interlocked.Exchange(ref _unavailableReported, 1) == 0);
                return false;
            }

            _watcher = watcher;
            DeckleOrchestrationSource.Log.ProcessTraceStarted();
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            ManagementEventWatcher? watcher = _watcher;
            _watcher = null;
            if (watcher is null) return;

            watcher.EventArrived -= OnEventArrived;
            try
            {
                watcher.Stop();
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            {
                DeckleOrchestrationSource.Log.ProcessTraceStopFailed(ex.GetType().Name, ex.Message);
            }
            finally
            {
                watcher.Dispose();
            }
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate) _disposed = true;
    }

    // Runs on a WMI thread: an exception escaping here would end the process,
    // so one unreadable event is logged and dropped.
    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            ManagementBaseObject data = e.NewEvent;
            int pid = (int)Convert.ToUInt32(data["ProcessID"]);
            string name = AppMatcher.NormalizeProcessName(data["ProcessName"] as string);
            string? path = ProcessNativeMethods.TryGetImagePath(pid);
            Started?.Invoke(new ProcessStarted(pid, name, path));
        }
        catch (Exception ex)
        {
            DeckleOrchestrationSource.Log.ProcessTraceEventUnreadable(ex.GetType().Name, ex.Message);
        }
    }

    // Internal so the observability test checks the exact pair a refusal emits.
    internal static void LogUnavailable(Exception ex, bool firstTime)
    {
        if (firstTime) DeckleOrchestrationSource.Log.ProcessTraceUnavailable();
        DeckleOrchestrationSource.Log.ProcessTraceUnavailableDetail(ex.GetType().Name, ex.Message);
    }
}
