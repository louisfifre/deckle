namespace Deckle.Diagnostics;

// One open user incident, as the registry hands it to its reader. Module and
// PageTag come from the host's declaration of the id; both are null for an id
// the host did not declare.
public sealed record UserIncident(string Id, string Reason, string? Module, string? PageTag);

// What is open now among the user incidents (UserIncidents): the state behind
// the tray dot and the menu lines, not a log. Each module already writes its own
// line when its incident opens and when it closes; it calls Open and Close beside
// that line, and this registry writes no line of its own for either.
//
// Process-wide, like the EventSource singletons: about thirty modules reach
// Deckle.Diagnostics and none of them is composed with a container. Instance is
// what they call; a test builds its own.
//
// Threading. Callers sit on threads of every kind (hotkey, recording, pool,
// timers) and some hold their own lock when they call. The set changes under a
// short lock of its own, and Changed is raised after it is released, on the
// caller's thread, only when the set really changed. The event carries nothing:
// a listener reads Snapshot, so a burst of changes reads as the latest state
// whatever order the notifications arrive in.
//
// Idempotent. Open keeps the first reason of an incident already open, Close of
// an incident not open does nothing, and neither raises Changed then. A module
// can therefore call them from every path that reaches its own open or close.
//
// A listener that throws is contained like a log sink that throws
// (DispatchEventListener): the module that opened the incident never sees the
// exception, the other listeners still run, the first failure is reported once
// with its cause, the failures after it are only counted, and the next
// notification every listener takes cleanly closes that report.
public sealed class UserIncidentRegistry
{
    public static UserIncidentRegistry Instance { get; } = new();

    private readonly object _gate = new();

    // Declaration order is the order of Snapshot, so a host ranks incidents by
    // declaring them in that order.
    private readonly List<Declaration> _declarations = [];
    private readonly Dictionary<string, OpenEntry> _open = new(StringComparer.Ordinal);
    private long _openSequence;

    private readonly object _faultGate = new();
    private bool _listenerFaulted;
    private int _listenerFailures;
    private int _failuresInFault;

    internal UserIncidentRegistry() { }

    /// <summary>
    /// Raised after the set of open incidents changed, on the thread that
    /// changed it, outside any lock. Read <see cref="Snapshot"/> for the state.
    /// </summary>
    public event Action? Changed;

    /// <summary>Failed listener calls since the process started.</summary>
    public int ListenerFailures
    {
        get { lock (_faultGate) return _listenerFailures; }
    }

    // The host says where an id belongs: the module it shows under and the
    // Settings page that carries the gesture. Declaring an id again replaces its
    // module and page and keeps its rank. An incident already open takes the
    // declaration at once, so a host that declares late still sees it.
    public void Declare(string id, string module, string pageTag)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(module);
        ArgumentException.ThrowIfNullOrEmpty(pageTag);

        bool affectsOpen;
        lock (_gate)
        {
            int index = _declarations.FindIndex(d => d.Id == id);
            var declaration = new Declaration(id, module, pageTag);
            if (index < 0) _declarations.Add(declaration);
            else _declarations[index] = declaration;
            affectsOpen = _open.ContainsKey(id);
        }
        if (affectsOpen) RaiseChanged();
    }

    public void Open(string id, string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        bool added;
        lock (_gate)
            added = _open.TryAdd(id, new OpenEntry(reason ?? "", ++_openSequence));
        if (added) RaiseChanged();
    }

    public void Close(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        bool removed;
        lock (_gate)
            removed = _open.Remove(id);
        if (removed) RaiseChanged();
    }

    public bool IsOpen(string id)
    {
        lock (_gate) return _open.ContainsKey(id);
    }

    // The open incidents, declared ones first in declaration order, then the
    // undeclared ones in the order they opened. A copy: the caller may keep it.
    public IReadOnlyList<UserIncident> Snapshot()
    {
        lock (_gate)
        {
            var result = new List<UserIncident>(_open.Count);
            foreach (Declaration declaration in _declarations)
            {
                if (_open.TryGetValue(declaration.Id, out OpenEntry entry))
                    result.Add(new UserIncident(declaration.Id, entry.Reason, declaration.Module, declaration.PageTag));
            }
            foreach (var (id, entry) in _open.OrderBy(pair => pair.Value.Sequence))
            {
                if (_declarations.Exists(d => d.Id == id)) continue;
                result.Add(new UserIncident(id, entry.Reason, Module: null, PageTag: null));
            }
            return result;
        }
    }

    // Each listener is called on its own, so one that throws neither reaches
    // the module nor keeps the next listener from its notification.
    private void RaiseChanged()
    {
        Action? changed = Changed;
        if (changed is null) return;

        Exception? firstError = null;
        string? firstListener = null;
        int failures = 0;
        foreach (Action listener in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                listener();
            }
            catch (Exception ex)
            {
                failures++;
                if (firstError is null)
                {
                    firstError = ex;
                    firstListener = NameOf(listener);
                }
            }
        }

        ReportListenerHealth(failures, firstListener, firstError);
    }

    private void ReportListenerHealth(int failures, string? listener, Exception? error)
    {
        bool opened = false;
        int recoveredAfter = -1;
        lock (_faultGate)
        {
            if (failures > 0)
            {
                _listenerFailures += failures;
                _failuresInFault += failures;
                if (!_listenerFaulted)
                {
                    _listenerFaulted = true;
                    opened = true;
                }
            }
            else if (_listenerFaulted)
            {
                _listenerFaulted = false;
                recoveredAfter = _failuresInFault;
                _failuresInFault = 0;
            }
        }

        if (opened)
        {
            DeckleDiagnosticsSource.Log.IncidentListenerFaulted(
                listener ?? "(unknown)", error?.GetType().Name ?? "(unknown)", error?.Message ?? "");
        }
        if (recoveredAfter >= 0)
            DeckleDiagnosticsSource.Log.IncidentListenerRecovered(recoveredAfter);
    }

    // Type.Method of the handler, the way a reader finds it in the code.
    private static string NameOf(Action listener)
    {
        var method = listener.Method;
        string type = method.DeclaringType?.Name ?? "(unknown)";
        return type + "." + method.Name;
    }

    private sealed record Declaration(string Id, string Module, string PageTag);

    private readonly record struct OpenEntry(string Reason, long Sequence);
}
