namespace Deckle.Orchestration;

// One listed app while it runs: the entry it matched when it started, and the
// processes of it still alive.
public sealed record ActiveSession(AppEntry Entry, IReadOnlyList<int> ProcessIds);

// Why a session ended, so a reader of the log can tell a game quitting
// from a settings change or a shutdown.
public enum SessionEndReason
{
    Exited,
    EntryRemoved,
    EntryDisabled,
    Shutdown,
}

// Turns "this process was seen" and "this process exited" into app sessions.
// Pure: no Win32, no I/O; the runtime feeds it from its event sources.
//
// A session begins with the first live process of an enabled entry and ends
// when its last one exits, or when the entry is removed or disabled. A
// process already tracked is ignored, so the same pid reported by several
// sources (start event, foreground, enumeration) counts once.
//
// It is the single serialization point of the runtime. One lock guards the
// state; events are queued under it and delivered outside it, one at a time
// and in order, by whichever thread is not already delivering. A handler that
// reports an exit while handling a start therefore never re-enters a handler:
// its event waits in the queue until the current one returns.
public sealed class AppSessionTracker
{
    private readonly Func<IReadOnlyList<AppEntry>> _entriesSnapshot;
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _entryIdByPid = new();
    private readonly Queue<Pending> _outbox = new();
    private bool _delivering;

    public AppSessionTracker(Func<IReadOnlyList<AppEntry>> entriesSnapshot)
    {
        ArgumentNullException.ThrowIfNull(entriesSnapshot);
        _entriesSnapshot = entriesSnapshot;
    }

    /// <summary>Every process that joins a session, the first one included. The runtime watches each for exit.</summary>
    public event Action<AppEntry, int>? ProcessTracked;

    /// <summary>The first process of an entry was seen.</summary>
    public event Action<AppEntry, int>? SessionStarted;

    /// <summary>The last process of an entry exited, the entry was removed or disabled, or every session was ended.</summary>
    public event Action<AppEntry, SessionEndReason>? SessionEnded;

    public IReadOnlyList<ActiveSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Values
                    .Select(s => new ActiveSession(s.Entry, [.. s.ProcessIds]))
                    .ToList();
            }
        }
    }

    public void OnProcessSeen(int pid, string? processName, string? executablePath)
    {
        // Matching reads the snapshot outside the lock: the registry swaps its
        // list by reference, so this is a complete list, old or new.
        AppEntry? entry = AppMatcher.Match(_entriesSnapshot(), processName, executablePath);
        if (entry is null) return;

        lock (_lock)
        {
            if (_entryIdByPid.ContainsKey(pid)) return;
            _entryIdByPid[pid] = entry.Id;

            if (_sessions.TryGetValue(entry.Id, out SessionState? session))
            {
                session.ProcessIds.Add(pid);
                AppEntry joined = session.Entry;
                _outbox.Enqueue(new Pending("process_tracked", joined.Id, () => ProcessTracked?.Invoke(joined, pid)));
            }
            else
            {
                _sessions[entry.Id] = new SessionState(entry, pid);
                _outbox.Enqueue(new Pending("process_tracked", entry.Id, () => ProcessTracked?.Invoke(entry, pid)));
                _outbox.Enqueue(new Pending("session_started", entry.Id, () => SessionStarted?.Invoke(entry, pid)));
            }
        }
        Deliver();
    }

    public void OnProcessExited(int pid)
    {
        lock (_lock)
        {
            if (!_entryIdByPid.Remove(pid, out string? entryId)) return;
            if (!_sessions.TryGetValue(entryId, out SessionState? session)) return;

            session.ProcessIds.Remove(pid);
            if (session.ProcessIds.Count == 0)
            {
                _sessions.Remove(entryId);
                EnqueueEnded(session.Entry, SessionEndReason.Exited);
            }
        }
        Deliver();
    }

    /// <summary>End every session whose entry is not in <paramref name="entries"/> or is disabled there.</summary>
    public void DropSessionsNotIn(IReadOnlyList<AppEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        // Id -> enabled. Built by hand rather than ToDictionary so a duplicated
        // id cannot throw here; any enabled copy keeps the session.
        var listed = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (AppEntry e in entries)
        {
            listed[e.Id] = listed.GetValueOrDefault(e.Id) || e.Enabled;
        }

        lock (_lock)
        {
            foreach (SessionState session in _sessions.Values.ToList())
            {
                bool present = listed.TryGetValue(session.Entry.Id, out bool enabled);
                if (present && enabled) continue;
                EndLocked(session, present ? SessionEndReason.EntryDisabled : SessionEndReason.EntryRemoved);
            }
        }
        Deliver();
    }

    // Separate from DropSessionsNotIn([]) so a shutdown is not reported as
    // every entry having been removed.
    public void EndAll()
    {
        lock (_lock)
        {
            foreach (SessionState session in _sessions.Values.ToList())
            {
                EndLocked(session, SessionEndReason.Shutdown);
            }
        }
        Deliver();
    }

    private void EndLocked(SessionState session, SessionEndReason reason)
    {
        _sessions.Remove(session.Entry.Id);
        foreach (int pid in session.ProcessIds) _entryIdByPid.Remove(pid);
        EnqueueEnded(session.Entry, reason);
    }

    private void EnqueueEnded(AppEntry ended, SessionEndReason reason) =>
        _outbox.Enqueue(new Pending("session_ended", ended.Id, () => SessionEnded?.Invoke(ended, reason)));

    // Single deliverer: the thread that finds nobody delivering drains the
    // queue; any other returns at once and its events are delivered in order
    // by the draining thread.
    private void Deliver()
    {
        lock (_lock)
        {
            if (_delivering) return;
            _delivering = true;
        }

        while (true)
        {
            Pending next;
            lock (_lock)
            {
                if (_outbox.Count == 0)
                {
                    _delivering = false;
                    return;
                }
                next = _outbox.Dequeue();
            }

            try
            {
                next.Invoke();
            }
            catch (Exception ex)
            {
                // Contained per event: rethrowing would leave the events queued
                // behind this one waiting for an unrelated caller, so a session
                // end (and the restore of its rules) could be delayed
                // indefinitely, and the exception would surface on a WMI or
                // thread-pool thread.
                DeckleOrchestrationSource.Log.SessionHandlerFailed(
                    next.Kind, next.EntryId, ex.GetType().Name, ex.Message);
            }
        }
    }

    // kind is the snake_case name the failure event reports.
    private readonly record struct Pending(string Kind, string EntryId, Action Invoke);

    private sealed class SessionState(AppEntry entry, int firstPid)
    {
        public AppEntry Entry { get; } = entry;

        public List<int> ProcessIds { get; } = [firstPid];
    }
}
