using Deckle.Diagnostics;
using Deckle.Install;

namespace Deckle.Anytype;

// Keeps the Anytype gateway running while Deckle lives, without ever tying the
// gateway's life to Deckle's (ADR-0008). At start it stages the gateway this
// Deckle carries, then adopts the gateway already listening if it is the same
// build, replaces it if it is another build, or starts one if none listens. It
// then watches the process and restarts it on a ladder if it stops.
//
// Disposing stops the supervision only: the gateway keeps serving through a
// Deckle relaunch or rebuild. It is replaced only when a Deckle carrying a
// different gateway build starts, which happens only when the Anytype code (or a
// library it carries) changed.
public sealed class GatewaySupervisor : IAsyncDisposable
{
    // Restart delays after an unexpected exit; past the last rung the supervisor
    // gives up until Deckle restarts.
    private static readonly TimeSpan[] RestartLadder =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60),
    ];

    // A gateway that served this long before stopping restarts from the first
    // rung: its exit is a new incident, not the same failure repeating.
    private static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(2);

    private readonly string _payloadDirectory;
    private readonly string _versionsRoot;
    private readonly IGatewayProcesses _processes;
    private readonly TimeSpan _listenTimeout;
    private readonly TimeSpan _stopTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _cts = new();
    private Task? _run;

    public GatewaySupervisor(string payloadDirectory)
        : this(
            payloadDirectory,
            Path.Combine(InstallPaths.DefaultProvidersDir, "Anytype Gateway", "versions"),
            new GatewayProcesses(),
            listenTimeout: TimeSpan.FromSeconds(20),
            // The gateway drains its requests for up to 5 s, then flushes its log.
            stopTimeout: TimeSpan.FromSeconds(10),
            delay: Task.Delay)
    {
    }

    internal GatewaySupervisor(
        string payloadDirectory,
        string versionsRoot,
        IGatewayProcesses processes,
        TimeSpan listenTimeout,
        TimeSpan stopTimeout,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _payloadDirectory = payloadDirectory;
        _versionsRoot = versionsRoot;
        _processes = processes;
        _listenTimeout = listenTimeout;
        _stopTimeout = stopTimeout;
        _delay = delay;
    }

    public void Start() => _run ??= Task.Run(() => RunAsync(_cts.Token));

    // Completes when the supervision ended by itself (gave up, conflict, not
    // provisioned) or after DisposeAsync.
    internal Task Completion => _run ?? Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        string workId = WorkId.Next();
        string outcome = "cancelled";
        string reason = "shutdown";
        DeckleAnytypeSource.Log.GatewaySupervisionStarted(workId);
        string step = "fingerprint";
        try
        {
            string? fingerprint = GatewayPayload.Fingerprint(_payloadDirectory);
            if (fingerprint is null)
            {
                DeckleAnytypeSource.Log.GatewayPayloadMissing(GatewayContract.PayloadFolderName);
                (outcome, reason) = ("failed", "payload_missing");
                return;
            }

            step = "stage";
            string executable = GatewayPayload.Stage(_payloadDirectory, fingerprint, _versionsRoot);
            DeckleAnytypeSource.Log.GatewayStaged(fingerprint);

            int restarts = 0;
            string launchReason = "start";
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                step = "reconcile";
                using IGatewayProcess? process = await EnsureRunningAsync(
                    executable, fingerprint, launchReason, workId, ct).ConfigureAwait(false);
                if (process is null)
                {
                    (outcome, reason) = ("failed", "endpoint_conflict");
                    return;
                }

                step = "prune";
                int removed = GatewayPayload.Prune(_versionsRoot, fingerprint);
                if (removed > 0)
                    DeckleAnytypeSource.Log.GatewayVersionsPruned(removed);

                step = "watch";
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                int exitCode = process.ExitCode ?? -1;
                double uptime = process.StartedAt is { } started
                    ? Math.Max(0, (DateTimeOffset.Now - started).TotalSeconds)
                    : -1;
                DeckleAnytypeSource.Log.GatewayExited(process.Id, exitCode, uptime, workId);

                if (exitCode == GatewayContract.ExitCodes.NotProvisioned)
                {
                    (outcome, reason) = ("stopped", "not_provisioned");
                    return;
                }

                if (uptime >= StableUptime.TotalSeconds)
                    restarts = 0;
                if (restarts >= RestartLadder.Length)
                {
                    DeckleAnytypeSource.Log.GatewaySupervisionGaveUp(restarts, workId);
                    (outcome, reason) = ("failed", "gave_up");
                    return;
                }

                TimeSpan wait = RestartLadder[restarts++];
                DeckleAnytypeSource.Log.GatewayRestartScheduled(restarts, (long)wait.TotalMilliseconds, workId);
                await _delay(wait, ct).ConfigureAwait(false);
                launchReason = "restart";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Deckle is shutting down; the gateway keeps running.
        }
        catch (Exception ex)
        {
            DeckleAnytypeSource.Log.GatewaySupervisionFailed(step, ex.GetType().Name, ex.Message);
            (outcome, reason) = ("failed", "exception");
        }
        finally
        {
            DeckleAnytypeSource.Log.GatewaySupervisionEnded(outcome, reason, workId);
        }
    }

    // Leaves exactly one gateway of this build listening, or answers null when
    // another program holds the port.
    private async Task<IGatewayProcess?> EnsureRunningAsync(
        string executable, string fingerprint, string launchReason, string workId, CancellationToken ct)
    {
        BackendListenerSnapshot listener = _processes.InspectListener();
        string? ownerPath = listener.State == BackendListenerState.Owned
            ? _processes.ExecutablePathOf(listener.ProcessId)
            : null;
        GatewayDecision decision = GatewayReconciliation.Decide(listener, ownerPath, executable, _versionsRoot);

        switch (decision.Action)
        {
            case GatewayAction.Adopt:
                IGatewayProcess? adopted = _processes.Open(decision.ProcessId);
                if (adopted is not null)
                {
                    DeckleAnytypeSource.Log.GatewayAdopted(decision.ProcessId, fingerprint, workId);
                    return adopted;
                }
                // It exited between the two reads: start a fresh one.
                break;

            case GatewayAction.Replace:
                DeckleAnytypeSource.Log.GatewayReplacing(decision.ProcessId, fingerprint, workId);
                await StopAsync(decision.ProcessId, ct).ConfigureAwait(false);
                launchReason = "replace";
                break;

            case GatewayAction.Conflict:
                DeckleAnytypeSource.Log.GatewayEndpointConflict(GatewayContract.Port, decision.Reason, decision.ProcessId);
                return null;
        }

        // A gateway that runs without owning the port is a start in progress or
        // one stuck before binding. This build's start gets the time to bind;
        // anything else is stopped so the new start does not race it.
        foreach (int processId in _processes.FindGatewayProcessIds())
        {
            string? path = _processes.ExecutablePathOf(processId);
            if (path is null || !GatewayPayload.IsStagedGateway(path, _versionsRoot))
                continue;
            if (string.Equals(path, executable, StringComparison.OrdinalIgnoreCase)
                && await WaitForListenerAsync(processId, process: null, ct).ConfigureAwait(false)
                && _processes.Open(processId) is { } warmed)
            {
                DeckleAnytypeSource.Log.GatewayAdopted(processId, fingerprint, workId);
                return warmed;
            }
            await StopAsync(processId, ct).ConfigureAwait(false);
        }

        IGatewayProcess spawned = _processes.Spawn(executable);
        DeckleAnytypeSource.Log.GatewayLaunched(spawned.Id, fingerprint, launchReason, workId);
        // Whether it binds or exits, the caller's watch reads the outcome.
        await WaitForListenerAsync(spawned.Id, spawned, ct).ConfigureAwait(false);
        return spawned;
    }

    private async Task<bool> WaitForListenerAsync(int processId, IGatewayProcess? process, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + _listenTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            BackendListenerSnapshot listener = _processes.InspectListener();
            if (listener.State == BackendListenerState.Owned && listener.ProcessId == processId)
                return true;
            if (process is { HasExited: true })
                return false;
            await _delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
        }
        return false;
    }

    // Asks the gateway to drain and exit, and ends it only if it does not.
    private async Task StopAsync(int processId, CancellationToken ct)
    {
        using IGatewayProcess? process = _processes.Open(processId);
        if (process is null || process.HasExited)
            return;

        bool asked = _processes.RequestStop(processId);
        if (asked)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_stopTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Did not exit in time: ended below.
            }
        }

        bool killed = process.TryKill(out string exType, out string exMessage);
        DeckleAnytypeSource.Log.GatewayStopForced(
            processId, asked ? "stop_timeout" : "no_stop_event", killed, exType, exMessage);
        if (killed)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_stopTimeout);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_run is not null)
        {
            try { await _run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}
