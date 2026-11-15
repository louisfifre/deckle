namespace Deckle.Projects;

// The in-memory truth the pages read about actions: for each project, the
// action running now (one at a time per project, spec) and the last result of
// every action that ended. The runner writes it, the window listens to Changed.
//
// Nothing here is saved: a result lives until its action runs again or Deckle
// closes (spec, Open 2, answer A).
public sealed class ActionRunBoard
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ActionRun> _active = [];
    private readonly Dictionary<string, Dictionary<ActionRef, ActionResult>> _results = [];

    // Raised with the project's id after every transition, on the thread that
    // made it; the view marshals to its own.
    public event Action<string>? Changed;

    public ActionRun? ActiveRun(string projectId)
    {
        lock (_gate) return _active.GetValueOrDefault(projectId);
    }

    public ActionResult? LastResult(string projectId, ActionRef action)
    {
        lock (_gate) return _results.TryGetValue(projectId, out var byAction) ? byAction.GetValueOrDefault(action) : null;
    }

    // Starts a run, or refuses with null when the project already runs an
    // action: one at a time per project, the other projects are free. Starting
    // an action clears its previous result (it stays until the action runs
    // again).
    public ActionRun? Begin(string projectId, ActionRun run)
    {
        lock (_gate)
        {
            if (_active.ContainsKey(projectId)) return null;
            _active[projectId] = run;
            if (_results.TryGetValue(projectId, out var byAction)) byAction.Remove(run.Action);
        }
        Changed?.Invoke(projectId);
        return run;
    }

    // The terminal that was asked for is the system's console, not the chosen
    // one: the running line says so. Raises Changed only when it changes.
    public void NoteFallback(string projectId)
    {
        lock (_gate)
        {
            if (!_active.TryGetValue(projectId, out ActionRun? run) || run.UsedFallback) return;
            _active[projectId] = run with { UsedFallback = true };
        }
        Changed?.Invoke(projectId);
    }

    // The run ended: its result is kept for its action and the project is free.
    // Does nothing when no run is active.
    public void End(string projectId, RunOutcome outcome, int? exitCode, DateTimeOffset endedAt)
    {
        lock (_gate)
        {
            if (!_active.Remove(projectId, out ActionRun? run)) return;
            if (!_results.TryGetValue(projectId, out var byAction)) _results[projectId] = byAction = [];
            byAction[run.Action] = new ActionResult(outcome, exitCode, endedAt, run.UsedFallback, run.Title);
        }
        Changed?.Invoke(projectId);
    }

    // The run never began after all (no terminal could be opened): the project
    // is free and no result is recorded, since nothing ran.
    public void Abort(string projectId)
    {
        lock (_gate)
        {
            if (!_active.Remove(projectId)) return;
        }
        Changed?.Invoke(projectId);
    }

    // The project left the store: nothing of it is kept.
    public void Forget(string projectId)
    {
        lock (_gate)
        {
            _active.Remove(projectId);
            _results.Remove(projectId);
        }
    }
}
