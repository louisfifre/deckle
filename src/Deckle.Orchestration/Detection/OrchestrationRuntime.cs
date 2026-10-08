using System.Diagnostics;

namespace Deckle.Orchestration;

// Notices when a listed, enabled app starts and stops, from events only:
// - the foreground monitor (an app coming to the front),
// - process start events (WMI, elevated Deckle only),
// - process exit, through the exit watch on each tracked process.
// Plus one enumeration of running processes on each reconcile, so apps already
// running at boot, or when an entry is added or re-enabled, are seen. That
// enumeration is a one-shot per settings change, never periodic: there is no
// timer anywhere in detection.
//
// One instance, owned by the App. Settings changes arrive on the UI thread
// (synchronously inside the registry's write lock); the reconcile they ask for
// runs on the thread pool, because connecting to WMI and enumerating processes
// must not block the UI. Sessions start and end on thread-pool or WMI threads,
// in order, through AppSessionTracker.
//
// Each session start applies the entry's rules through ProfileTransaction;
// its end, and Stop, restore them. Stop restores every active profile, so a
// shutdown never leaves a rule applied.
public sealed class OrchestrationRuntime : IDisposable
{
    private readonly OrchestrationSettingsService _settings;
    private readonly IForegroundMonitor _foreground;
    private readonly IProcessStartSource _processStarts;
    private readonly ProcessExitWatch _exitWatch;
    private readonly AppSessionTracker _tracker;
    private readonly IReadOnlyList<IRuleEffect> _effects;

    // Active profiles by entry id. The lock is held while a profile applies,
    // so Stop cannot restore the list while a new profile is half-applied.
    private readonly Dictionary<string, AppliedProfile> _profiles = new(StringComparer.Ordinal);
    private readonly object _profilesLock = new();

    // Guards Start/Stop/Dispose.
    private readonly object _lifecycle = new();

    // Serializes reconcile passes, and Stop against a pass in flight.
    private readonly object _reconcileLock = new();

    private volatile bool _started;
    private bool _disposed;
    private bool _processStartsRunning;

    // Elevation does not change while Deckle runs: once WMI refused, it is not
    // asked again.
    private bool _processStartsRefused;

    // 1 while a reconcile is queued and not yet begun: settings changes in a
    // burst cost one pass, not one each.
    private int _reconcilePending;

    private DetectionStatus _status;

    /// <summary>Read by the settings page through the App, which sets it. Null when no runtime is composed.</summary>
    public static Func<DetectionStatus>? StatusProvider { get; set; }

    public OrchestrationRuntime(
        OrchestrationSettingsService settings,
        IForegroundMonitor foreground,
        IProcessStartSource processStarts,
        ProcessExitWatch exitWatch,
        IReadOnlyList<IRuleEffect>? effects = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(processStarts);
        ArgumentNullException.ThrowIfNull(exitWatch);

        _settings = settings;
        _foreground = foreground;
        _processStarts = processStarts;
        _exitWatch = exitWatch;
        _effects = effects ?? []; // none: detection only, no rule applied

        _tracker = new AppSessionTracker(() => _settings.Current.Entries);
        _tracker.ProcessTracked += OnProcessTracked;
        _tracker.SessionStarted += OnSessionStarted;
        _tracker.SessionEnded += OnSessionEnded;
        _processStarts.Started += OnProcessStarted;

        // Until WMI is first asked, elevation predicts its answer, so the page
        // shows the right advisory before the first reconcile has run.
        _status = new DetectionStatus(
            ProcessTraceAvailable: Environment.IsPrivilegedProcess,
            ForegroundMonitorPresent: foreground is not NullForegroundMonitor);
    }

    /// <summary>A listed app started: the first process of an enabled entry was seen.</summary>
    public event Action<AppEntry, int>? SessionStarted;

    /// <summary>A listed app stopped, was removed or disabled, or the runtime stopped.</summary>
    public event Action<AppEntry>? SessionEnded;

    public event Action? StatusChanged;

    public DetectionStatus Status => Volatile.Read(ref _status);

    public IReadOnlyList<ActiveSession> Sessions => _tracker.Sessions;

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_started || _disposed) return;
            _started = true;
            _foreground.ForegroundChanged += OnForegroundChanged;
            _settings.Changed += RequestReconcile;
        }
        DeckleOrchestrationSource.Log.RuntimeStarted();
        RequestReconcile();
    }

    /// <summary>
    /// Bring detection in line with the registry: start or stop WMI start
    /// events, end sessions of removed or disabled entries, and offer every
    /// running process to the tracker once. Runs on the calling thread.
    /// </summary>
    public void Reconcile()
    {
        lock (_reconcileLock)
        {
            if (!_started) return;

            IReadOnlyList<AppEntry> entries = _settings.Current.Entries;
            int enabled = entries.Count(e => e.Enabled);

            if (enabled > 0)
            {
                if (!_processStartsRunning && !_processStartsRefused)
                {
                    _processStartsRunning = _processStarts.Start();
                    _processStartsRefused = !_processStartsRunning;
                    SetProcessTraceAvailable(_processStartsRunning);
                }
            }
            else if (_processStartsRunning)
            {
                _processStarts.Stop();
                _processStartsRunning = false;
            }

            _tracker.DropSessionsNotIn(entries);

            int seen = enabled > 0 ? OfferRunningProcesses() : 0;
            DeckleOrchestrationSource.Log.ReconcileDetail(enabled, _processStartsRunning, seen);
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            if (!_started) return;
            _started = false;
            _foreground.ForegroundChanged -= OnForegroundChanged;
            _settings.Changed -= RequestReconcile;

            // Restore before ending sessions: a session end delivered by
            // another thread may still be queued in the tracker, and Deckle
            // must not exit with a game's HDR left on. This runs on the
            // caller's thread (the UI thread at shutdown), because the restore
            // has to finish before the process does.
            RestoreAllProfiles();

            lock (_reconcileLock)
            {
                if (_processStartsRunning) _processStarts.Stop();
                _processStartsRunning = false;

                // Every session ends; their profiles are already restored.
                _tracker.DropSessionsNotIn([]);
                _exitWatch.UnwatchAll();
            }
        }
        DeckleOrchestrationSource.Log.RuntimeStopped();
    }

    public void Dispose()
    {
        Stop();
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _processStarts.Started -= OnProcessStarted;
        _processStarts.Dispose();
        _exitWatch.Dispose();
    }

    private void RequestReconcile()
    {
        if (Interlocked.Exchange(ref _reconcilePending, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            // Cleared before the pass reads the registry, so a change made
            // during the pass queues another one.
            Volatile.Write(ref _reconcilePending, 0);
            Reconcile();
        });
    }

    // One-shot enumeration; see the header. Processes that exit or refuse a
    // query mid-way are skipped.
    private int OfferRunningProcesses()
    {
        int self = Environment.ProcessId;
        int seen = 0;
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == self || process.Id == 0) continue;
                string name;
                try
                {
                    name = process.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                _tracker.OnProcessSeen(process.Id, name, ProcessNativeMethods.TryGetImagePath(process.Id));
                seen++;
            }
        }
        return seen;
    }

    private void OnForegroundChanged(ForegroundChange change)
    {
        if (!_started) return;
        _tracker.OnProcessSeen(change.ProcessId, change.ProcessName, change.ExecutablePath);
    }

    private void OnProcessStarted(ProcessStarted started)
    {
        if (!_started) return;
        _tracker.OnProcessSeen(started.ProcessId, started.ProcessName, started.ExecutablePath);
    }

    // Every tracked process is watched, not only the first of a session:
    // otherwise a session whose first process exits early (a launcher) would
    // never see its last one go.
    private void OnProcessTracked(AppEntry entry, int pid) =>
        _exitWatch.Watch(pid, _tracker.OnProcessExited);

    // Rules apply at launch, from the entry as it was when the session began:
    // editing an entry's rules while its app runs changes the next launch, not
    // this one. Runs on the thread that delivered the start (thread pool or
    // WMI), never the UI thread.
    private void OnSessionStarted(AppEntry entry, int pid)
    {
        LogSessionStarted(entry, pid);
        ApplyProfile(entry);
        SessionStarted?.Invoke(entry, pid);
    }

    private void OnSessionEnded(AppEntry entry)
    {
        LogSessionEnded(entry);
        RestoreProfile(entry.Id);
        SessionEnded?.Invoke(entry);
    }

    private void ApplyProfile(AppEntry entry)
    {
        lock (_profilesLock)
        {
            // Stop has begun: nothing new is applied that nobody would restore.
            if (!_started) return;

            // A profile left from a session whose end was never seen is
            // restored first, so the new one captures the true prior state.
            if (_profiles.Remove(entry.Id, out AppliedProfile? stale)) stale.Restore();

            AppliedProfile profile = ProfileTransaction.Apply(entry, _effects);
            if (profile.Changes > 0) _profiles[entry.Id] = profile;
        }
    }

    private void RestoreProfile(string entryId)
    {
        lock (_profilesLock)
        {
            if (_profiles.Remove(entryId, out AppliedProfile? profile)) profile.Restore();
        }
    }

    private void RestoreAllProfiles()
    {
        lock (_profilesLock)
        {
            foreach (AppliedProfile profile in _profiles.Values) profile.Restore();
            _profiles.Clear();
        }
    }

    private void SetProcessTraceAvailable(bool available)
    {
        DetectionStatus current = Status;
        if (current.ProcessTraceAvailable == available) return;
        Volatile.Write(ref _status, current with { ProcessTraceAvailable = available });
        StatusChanged?.Invoke();
    }

    // Internal so the observability test checks the exact pair a session emits.
    internal static void LogSessionStarted(AppEntry entry, int pid)
    {
        DeckleOrchestrationSource.Log.SessionStarted();
        DeckleOrchestrationSource.Log.SessionStartedDetail(entry.Id, entry.DisplayName, entry.ProcessName, pid);
    }

    internal static void LogSessionEnded(AppEntry entry)
    {
        DeckleOrchestrationSource.Log.SessionEnded();
        DeckleOrchestrationSource.Log.SessionEndedDetail(entry.Id, entry.DisplayName);
    }
}
