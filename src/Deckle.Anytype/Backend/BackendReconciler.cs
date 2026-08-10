namespace Deckle.Anytype;

internal sealed record BackendReconciliationResult(
    BackendStartOutcome Outcome,
    IBackendProcess? Process,
    string ReconciliationId,
    string Decision);

internal interface IBackendReconciler
{
    Task<BackendReconciliationResult> ReconcileAsync(string trigger, CancellationToken ct);
}

// One inspect/adopt/spawn transaction. The named lease spans warm-up so a
// successor cannot observe a temporarily unbound endpoint and launch a second
// serve. Readiness is accepted only after two TCP-owner snapshots around health.
internal sealed class BackendReconciler : IBackendReconciler
{
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
                _ => ReconcileProtected(id, ct),
                ct).ConfigureAwait(false);
            DeckleAnytypeSource.Log.BackendReconciliationCompleted(
                id, result.Decision, result.Process?.Id ?? 0,
                _time.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeckleAnytypeSource.Log.BackendReconciliationCancelled(
                id, _time.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    private BackendReconciliationResult ReconcileProtected(string id, CancellationToken ct)
    {
        long started = _time.GetTimestamp();
        IBackendProcess? spawned = null;
        bool spawnAttempted = false;
        try
        {
            while (_time.GetElapsedTime(started) < ReadinessTimeout)
            {
                ct.ThrowIfCancellationRequested();

                BackendListenerSnapshot first = _listener.Inspect();
                if (first.State is BackendListenerState.Ambiguous or BackendListenerState.Failed)
                    return Conflict(id, first, null);

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
                    DeckleAnytypeSource.Log.BackendStopped();
                    DeckleAnytypeSource.Log.BackendStoppedDetail(
                        spawned.Id, spawned.ExitCode,
                        Math.Max(0, (DateTimeOffset.UtcNow - spawned.StartedAt).TotalSeconds));
                    return Result(BackendStartOutcome.StartRejected, null, id, "start-rejected");
                }

                IReadOnlyList<IBackendProcess> running = _processes.FindRunning(
                    _provider.TrustedExecutablePaths());
                try
                {
                    bool oneIsAlive = running.Any(process => !process.HasExited);
                    if (!oneIsAlive && spawned is null && !spawnAttempted)
                    {
                        BackendProcessSpec? spec = _provider.ResolveActiveSpec();
                        if (spec is null)
                        {
                            DeckleAnytypeSource.Log.BackendNotProvisioned();
                            return Result(BackendStartOutcome.NotProvisioned, null, id, "not-provisioned");
                        }

                        ct.ThrowIfCancellationRequested();
                        DeckleAnytypeSource.Log.BackendStarting();
                        spawned = _processes.Spawn(spec);
                        spawnAttempted = true;
                        if (spawned is null)
                            return Result(BackendStartOutcome.StartRejected, null, id, "start-rejected");
                    }
                }
                finally
                {
                    foreach (IBackendProcess process in running) process.Dispose();
                }

                _time.Delay(ProbeInterval, ct);
            }

            DeckleAnytypeSource.Log.BackendStartTimedOut();
            spawned?.Dispose();
            spawned = null;
            return Result(BackendStartOutcome.TimedOut, null, id, "timed-out");
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
        if (owner is null) return Conflict(id, first, "owner process is unreadable");

        bool transferOwner = !ReferenceEquals(owner, spawned);
        try
        {
            if (!IsTrusted(owner.ExecutablePath))
                return Conflict(id, first, owner.ExecutablePath);
            if (owner.HasExited) return null;

            bool healthy = _health.IsHealthyAsync(ct).GetAwaiter().GetResult();
            BackendListenerSnapshot second = _listener.Inspect();
            DeckleAnytypeSource.Log.BackendListenerObserved(
                id, first.ProcessId, second.ProcessId, healthy,
                owner.ExecutablePath);

            if (!healthy || second.State != BackendListenerState.Owned ||
                second.ProcessId != first.ProcessId || owner.HasExited)
                return null;

            string mode = ReferenceEquals(owner, spawned) ? "spawned" : "adopted";
            DeckleAnytypeSource.Log.BackendProcessAttached(owner.Id, mode);
            DeckleAnytypeSource.Log.BackendReady();
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
        string? detail)
    {
        DeckleAnytypeSource.Log.BackendEndpointConflict();
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
