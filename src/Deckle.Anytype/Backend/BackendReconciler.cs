namespace Deckle.Anytype;

// PendingProcessId names the live trusted serve a timed-out reconciliation
// waited on without it ever serving; 0 when none was seen.
internal sealed record BackendReconciliationResult(
    BackendStartOutcome Outcome,
    IBackendProcess? Process,
    string ReconciliationId,
    string Decision,
    int PendingProcessId = 0);

// Closed vocabulary of what asks for a reconciliation, the `trigger` of
// BackendReconciliationStarted. Startup is the first start of the session; the
// other two come from the restart ladder, whose attempts belong to an incident
// that is already open.
internal static class BackendTriggers
{
    internal const string Startup = "startup";
    internal const string ProcessExit = "process-exit";
    internal const string StartFailed = "start-failed";

    internal static bool IsRestart(string trigger) => trigger != Startup;
}

internal interface IBackendReconciler
{
    Task<BackendReconciliationResult> ReconcileAsync(string trigger, CancellationToken ct);
}

// One inspect/adopt/spawn transaction. The named lease spans warm-up so a
// successor cannot observe a temporarily unbound endpoint and launch a second
// serve. Readiness is accepted only after two TCP-owner snapshots around health.
internal sealed class BackendReconciler : IBackendReconciler
{
    // On 0.5.0 the serve auto-logs-in with its stored account key about 2.6 s
    // after it starts, and the REST listener answers 200 only after that login,
    // so a probe before then is refused by design. 20 s is several times that
    // delay, yet short enough to surface a backend that never binds.
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(500);

    private readonly IBackendProviderCatalog _provider;
    private readonly IBackendProcessHost _processes;
    private readonly IBackendListenerOwner _listener;
    private readonly IBackendHealthProbe _health;
    private readonly IBackendReconciliationCoordinator _coordinator;
    private readonly IBackendTime _time;

    internal BackendReconciler(
        IBackendProviderCatalog provider,
        IBackendProcessHost processes,
        IBackendListenerOwner listener,
        IBackendHealthProbe health,
        IBackendReconciliationCoordinator coordinator,
        IBackendTime time)
    {
        _provider = provider;
        _processes = processes;
        _listener = listener;
        _health = health;
        _coordinator = coordinator;
        _time = time;
    }

    internal static BackendReconciler CreateDefault(IBackendHealthProbe health) => new(
        BackendInstallation.ProviderCatalog,
        new BackendProcessHost(),
        new BackendListenerOwner(),
        health,
        new BackendReconciliationLease(),
        new BackendTime());

    public async Task<BackendReconciliationResult> ReconcileAsync(
        string trigger,
        CancellationToken ct)
    {
        string id = Guid.NewGuid().ToString("N");
        long started = _time.GetTimestamp();
        DeckleAnytypeSource.Log.BackendReconciliationStarted(id, trigger);
        try
        {
            BackendReconciliationResult result = await _coordinator.RunAsync(
                id,
                _ => ReconcileProtected(id, BackendTriggers.IsRestart(trigger), ct),
                ct).ConfigureAwait(false);
            DeckleAnytypeSource.Log.BackendReconciliationCompleted(
                id, result.Decision, result.Process?.Id ?? result.PendingProcessId,
                _time.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeckleAnytypeSource.Log.BackendReconciliationCancelled(
                id, _time.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            // Without this end the Started above dangles; the caller decides
            // what the fault means, so it still propagates.
            DeckleAnytypeSource.Log.BackendReconciliationFailed(
                id, ex.GetType().Name, ex.Message,
                _time.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    private BackendReconciliationResult ReconcileProtected(string id, bool inRestart, CancellationToken ct)
    {
        long started = _time.GetTimestamp();
        IBackendProcess? spawned = null;
        bool spawnAttempted = false;
        int pendingId = 0;
        try
        {
            while (_time.GetElapsedTime(started) < ReadinessTimeout)
            {
                ct.ThrowIfCancellationRequested();

                BackendListenerSnapshot first = _listener.Inspect();
                if (first.State == BackendListenerState.Owned) pendingId = first.ProcessId;
                if (first.State is BackendListenerState.Ambiguous or BackendListenerState.Failed)
                    return Conflict(
                        id, first,
                        first.State == BackendListenerState.Ambiguous
                            ? "listener_ambiguous"
                            : "listener_query_failed",
                        null);

                if (first.State == BackendListenerState.Owned)
                {
                    BackendReconciliationResult? adopted = TryAdoptOwner(id, first, spawned, ct);
                    if (adopted is not null)
                    {
                        if (ReferenceEquals(adopted.Process, spawned))
                        {
                            spawned = null;
                        }
                        else
                        {
                            spawned?.Dispose();
                            spawned = null;
                        }
                        return adopted;
                    }
                }
                else if (spawned is not null && spawned.HasExited)
                {
                    if (inRestart) BackendProcessReport.AttemptStopped(spawned, id);
                    else BackendProcessReport.Stopped(spawned, id);
                    return Result(BackendStartOutcome.StartRejected, null, id, "start-rejected");
                }

                IReadOnlyList<IBackendProcess> running = _processes.FindRunning(
                    _provider.TrustedExecutablePaths());
                try
                {
                    IBackendProcess? alive = running.FirstOrDefault(process => !process.HasExited);
                    if (alive is not null) pendingId = alive.Id;
                    if (alive is null && spawned is null && !spawnAttempted)
                    {
                        BackendProcessSpec? spec = _provider.ResolveActiveSpec();
                        if (spec is null)
                        {
                            DeckleAnytypeSource.Log.BackendNotProvisioned();
                            return Result(BackendStartOutcome.NotProvisioned, null, id, "not-provisioned");
                        }

                        ct.ThrowIfCancellationRequested();
                        if (inRestart) DeckleAnytypeSource.Log.BackendRestartAttemptStarting(id);
                        else DeckleAnytypeSource.Log.BackendStarting(id);
                        spawned = _processes.Spawn(spec, inRestart);
                        spawnAttempted = true;
                        if (spawned is null)
                            return Result(BackendStartOutcome.StartRejected, null, id, "start-rejected");
                        pendingId = spawned.Id;
                    }
                }
                finally
                {
                    foreach (IBackendProcess process in running) process.Dispose();
                }

                _time.Delay(ProbeInterval, ct);
            }

            // Disposing releases Deckle's handle only: the serve, if alive,
            // keeps running and the next reconciliation waits on it again.
            if (inRestart) DeckleAnytypeSource.Log.BackendRestartAttemptTimedOut(pendingId, id);
            else DeckleAnytypeSource.Log.BackendStartTimedOut(pendingId, id);
            spawned?.Dispose();
            spawned = null;
            return new(BackendStartOutcome.TimedOut, null, id, "timed-out", pendingId);
        }
        finally
        {
            spawned?.Dispose();
        }
    }

    private BackendReconciliationResult? TryAdoptOwner(
        string id,
        BackendListenerSnapshot first,
        IBackendProcess? spawned,
        CancellationToken ct)
    {
        IBackendProcess? owner = spawned is not null && spawned.Id == first.ProcessId
            ? spawned
            : _processes.Open(first.ProcessId);
        if (owner is null) return Conflict(id, first, "owner_unreadable", "owner process is unreadable");

        bool transferOwner = !ReferenceEquals(owner, spawned);
        try
        {
            // The detail names the foreign program, not where it lives: its path
            // embeds the Windows user name.
            if (!IsTrusted(owner.ExecutablePath))
                return Conflict(id, first, "owner_untrusted", Path.GetFileName(owner.ExecutablePath));
            if (owner.HasExited) return null;

            bool healthy = _health.ProbeAsync(ct).GetAwaiter().GetResult().Healthy;
            BackendListenerSnapshot second = _listener.Inspect();
            DeckleAnytypeSource.Log.BackendListenerObserved(
                id, first.ProcessId, second.ProcessId, healthy,
                Path.GetFileName(owner.ExecutablePath));

            if (!healthy || second.State != BackendListenerState.Owned ||
                second.ProcessId != first.ProcessId || owner.HasExited)
                return null;

            string mode = ReferenceEquals(owner, spawned) ? "spawned" : "adopted";
            DeckleAnytypeSource.Log.BackendProcessAttached(owner.Id, mode);
            DeckleAnytypeSource.Log.BackendReady(id);
            BackendReconciliationResult result = Result(
                mode == "spawned" ? BackendStartOutcome.Started : BackendStartOutcome.AlreadyRunning,
                owner, id, mode);
            transferOwner = false;
            return result;
        }
        finally
        {
            if (transferOwner) owner.Dispose();
        }
    }

    private BackendReconciliationResult Conflict(
        string id,
        BackendListenerSnapshot snapshot,
        string reason,
        string? detail)
    {
        DeckleAnytypeSource.Log.BackendEndpointConflict(reason, snapshot.ProcessId);
        DeckleAnytypeSource.Log.BackendEndpointConflictDetail(
            id, snapshot.ProcessId, detail ?? snapshot.Error ?? snapshot.State.ToString());
        return Result(BackendStartOutcome.EndpointConflict, null, id, "endpoint-conflict");
    }

    private bool IsTrusted(string executablePath) => _provider.TrustedExecutablePaths()
        .Any(path => string.Equals(
            Path.GetFullPath(path), Path.GetFullPath(executablePath),
            StringComparison.OrdinalIgnoreCase));

    private static BackendReconciliationResult Result(
        BackendStartOutcome outcome,
        IBackendProcess? process,
        string id,
        string decision) => new(outcome, process, id, decision);
}
