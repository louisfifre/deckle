namespace Deckle.Anytype;

// PendingProcessId names the live trusted serve a timed-out reconciliation
// waited on without it ever serving; 0 when none was seen. Replacement is null
// unless the caller asked for a serve to be replaced.
internal sealed record BackendReconciliationResult(
    BackendStartOutcome Outcome,
    IBackendProcess? Process,
    string ReconciliationId,
    string Decision,
    int PendingProcessId = 0,
    BackendReplacement? Replacement = null);

// What became of a request to replace a serve. Declined means the identity
// re-check said no and the serve was left alone (the reconciler wrote why);
// Refused means the ending was tried and Windows said no, or the serve outlived
// it, with the cause for the supervisor to log.
internal enum BackendReplaceOutcome { Ended, Declined, Refused }

internal sealed record BackendReplacement(
    BackendReplaceOutcome Outcome,
    string ExType = "",
    string ExMessage = "");

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
    // A reconciliation never ends a serve on its own: it waits on a live trusted
    // one. replaceProcessId, when not 0, is the one exception, asked for by the
    // supervisor, which holds the memory across attempts that this transaction
    // lacks. The reconciler re-proves that serve's identity before ending it.
    Task<BackendReconciliationResult> ReconcileAsync(
        string trigger, CancellationToken ct, int replaceProcessId = 0);
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

    // A killed process is gone from the kernel within milliseconds; the bound
    // only keeps a serve that survives its kill from holding the lease.
    private static readonly TimeSpan TerminationWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminationPoll = TimeSpan.FromMilliseconds(100);

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
        CancellationToken ct,
        int replaceProcessId = 0)
    {
        string id = Guid.NewGuid().ToString("N");
        long started = _time.GetTimestamp();
        DeckleAnytypeSource.Log.BackendReconciliationStarted(id, trigger);
        try
        {
            BackendReconciliationResult result = await _coordinator.RunAsync(
                id,
                _ =>
                {
                    // Inside the lease, before the loop: the ending and the
                    // spawn that replaces it form one transaction, so no other
                    // Deckle can slip a serve between them.
                    BackendReplacement? replacement = replaceProcessId != 0
                        ? ReplaceWedgedServe(id, replaceProcessId, ct)
                        : null;
                    BackendReconciliationResult reconciled = ReconcileProtected(
                        id, BackendTriggers.IsRestart(trigger), ct);
                    return replacement is null ? reconciled : reconciled with { Replacement = replacement };
                },
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

    // Ends a serve the supervisor found alive and unready for too long. The ask
    // is old news by the time the lease is held, so identity is proven again
    // here: the pid must still be a live process of a trusted provider and must
    // not own the listener (a serve that bound it became ready after all).
    // Anything short of that leaves it alone, and the loop below behaves as it
    // always has. Never throws for a refusal; cancellation still propagates.
    private BackendReplacement ReplaceWedgedServe(string id, int processId, CancellationToken ct)
    {
        using IBackendProcess? serve = _processes.Open(processId);
        if (serve is null) return Declined(id, processId, "gone");
        if (serve.HasExited) return Declined(id, processId, "exited");

        bool trusted;
        try
        {
            trusted = IsTrusted(serve.ExecutablePath);
        }
        catch (Exception ex) when (ex is
            ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            trusted = false;
        }
        if (!trusted) return Declined(id, processId, "not_trusted");

        BackendListenerSnapshot listener = _listener.Inspect();
        if (listener.State == BackendListenerState.Owned && listener.ProcessId == processId)
            return Declined(id, processId, "owns_listener");
        // A table that cannot be read, or shared by several owners, cannot show
        // that the serve does not own the port.
        if (listener.State is BackendListenerState.Failed or BackendListenerState.Ambiguous)
            return Declined(id, processId, "listener_unreadable");

        if (!serve.TryTerminate(out string exType, out string exMessage))
            return new(BackendReplaceOutcome.Refused, exType, exMessage);

        long waitStarted = _time.GetTimestamp();
        while (!serve.HasExited)
        {
            if (_time.GetElapsedTime(waitStarted) >= TerminationWait)
                return new(
                    BackendReplaceOutcome.Refused, "TimeoutException",
                    $"the process was still running {TerminationWait.TotalSeconds:F0} s after it was ended");
            _time.Delay(TerminationPoll, ct);
        }
        return new(BackendReplaceOutcome.Ended);
    }

    private static BackendReplacement Declined(string id, int processId, string reason)
    {
        DeckleAnytypeSource.Log.BackendWedgedServeEndDeclined(processId, reason, id);
        return new(BackendReplaceOutcome.Declined);
    }

    private BackendReconciliationResult ReconcileProtected(string id, bool inRestart, CancellationToken ct)
    {
        long started = _time.GetTimestamp();
        IBackendProcess? spawned = null;
        bool spawnAttempted = false;
        int pendingId = 0;
        string? inspectionError = null;
        try
        {
            while (_time.GetElapsedTime(started) < ReadinessTimeout)
            {
                ct.ThrowIfCancellationRequested();

                BackendListenerSnapshot first = _listener.Inspect();

                // A failed read of the TCP table says nothing about who owns the
                // endpoint (it can fail for a moment under load), so it is no
                // conflict: the supervisor gives up for the session on a
                // conflict. Look again until the window closes, and spawn
                // nothing meanwhile, since the listener may well be owned.
                if (first.State == BackendListenerState.Failed)
                {
                    inspectionError = first.Error ?? "listener inspection failed";
                    _time.Delay(ProbeInterval, ct);
                    continue;
                }

                inspectionError = null;
                if (first.State == BackendListenerState.Owned) pendingId = first.ProcessId;
                if (first.State == BackendListenerState.Ambiguous)
                    return Conflict(id, first, "listener_ambiguous", null);

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
            // The cause rides the decision, so the supervisor's log line for the
            // failed attempt says the table could not be read, not that a serve was slow.
            string decision = inspectionError is null
                ? "timed-out"
                : $"listener_query_failed: {inspectionError}";
            return new(BackendStartOutcome.TimedOut, null, id, decision, pendingId);
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
