using System.Diagnostics;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

public enum BackendStartOutcome
{
    AlreadyRunning,
    Started,
    NotProvisioned,
    StartRejected,
    TimedOut,
    EndpointConflict,
}

// Owns one initial reconciliation and the process-exit watch that follows it.
// Disposal cancels and drains both operations; it never terminates the backend.
public sealed class BackendSupervisor : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan[] RestartBackoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];
    private static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(5);

    // Three failed restarts span about 22 s of backoff: past a transient blip,
    // early enough that the warning still explains a missing Anytype.
    private const int RestartsFailingThreshold = 3;

    // A live process does not prove a serving REST listener: a wedged serve
    // keeps its handle while every call times out. The probe runs on real time,
    // not IBackendTime, so the restart ladder's controlled clock stays untouched.
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(30);
    private const int UnresponsiveThreshold = 3;

    // How long one serve may stay alive without serving before the next
    // reconciliation is asked to replace it. A normal login binds REST about
    // 2.6 s after the start, so five minutes leaves room for a slow migration
    // and still does not leave Anytype dead for hours: on 2026-10-24 a serve
    // stuck in account_transition was waited on 352 times over 8 hours, because
    // the reconciler never spawns beside a live trusted serve.
    internal static readonly TimeSpan WedgedServeGrace = TimeSpan.FromMinutes(5);

    private readonly IBackendReconciler _reconciler;
    private readonly IBackendHealthProbe _health;
    private readonly IBackendTime _time;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _gate = new();
    private Task<BackendStartOutcome>? _initializationTask;
    private Task? _watchTask;
    private Task? _disposalTask;
    private bool _disposed;

    public BackendSupervisor(BackendHealthProbe health)
        : this(BackendReconciler.CreateDefault(health), health, new BackendTime())
    {
    }

    internal BackendSupervisor(
        IBackendReconciler reconciler,
        IBackendHealthProbe health,
        IBackendTime time)
    {
        _reconciler = reconciler;
        _health = health;
        _time = time;
    }

    public Task<BackendStartOutcome> EnsureRunningAsync(CancellationToken ct = default)
    {
        Task<BackendStartOutcome> initialization;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            initialization = _initializationTask ??= InitializeAsync(_lifetimeCts.Token);
        }

        // Initialization belongs to the supervisor, not to whichever caller
        // happened to arrive first. A caller may abandon only its own wait;
        // disposal remains the sole cancellation boundary for reconciliation.
        return ct.CanBeCanceled ? initialization.WaitAsync(ct) : initialization;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposalTask is not null)
                return new ValueTask(_disposalTask);

            _disposed = true;
            _lifetimeCts.Cancel();
            _disposalTask = DisposeCoreAsync(_initializationTask);
            return new ValueTask(_disposalTask);
        }
    }

    private async Task DisposeCoreAsync(Task<BackendStartOutcome>? initialization)
    {
        Task? watch;
        try
        {
            await IgnoreCancellation(initialization).ConfigureAwait(false);
            lock (_gate) watch = _watchTask;
            await IgnoreCancellation(watch).ConfigureAwait(false);
        }
        finally
        {
            // A reconciliation or watch fault must not bypass ownership cleanup.
            _health.Dispose();
            _lifetimeCts.Dispose();
        }
    }

    private async Task<BackendStartOutcome> InitializeAsync(CancellationToken lifetimeToken)
    {
        // The startup wait is the first minutes of a serve's unready time, so
        // the incident it opens inherits both the pid and this timestamp.
        long startedAt = _time.GetTimestamp();
        BackendReconciliationResult result = await _reconciler
            .ReconcileAsync(BackendTriggers.Startup, lifetimeToken)
            .ConfigureAwait(false);
        if (result.Process is not null)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    result.Process.Dispose();
                    return result.Outcome;
                }
                IBackendProcess process = result.Process;
                _watchTask = SuperviseAsync(ct => WatchAsync(process, ct), _lifetimeCts.Token);
            }
        }
        else if (result.Outcome is BackendStartOutcome.EndpointConflict)
        {
            // Another program holds the backend's port: nothing in Deckle will
            // free it, so the person has to.
            OpenUserIncident("endpoint_conflict");
        }
        else if (result.Outcome is BackendStartOutcome.StartRejected or BackendStartOutcome.TimedOut)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    // The incident shares the id of the start that opened it.
                    string startId = result.ReconciliationId;
                    int pendingId = result.Outcome is BackendStartOutcome.TimedOut
                        ? result.PendingProcessId : 0;
                    _watchTask = SuperviseAsync(
                        ct => RecoverAsync(startId, pendingId, startedAt, ct), _lifetimeCts.Token);
                }
            }
        }
        return result.Outcome;
    }

    // Every supervision run ends with exactly one line: stopped on Deckle's
    // exit, gave up (logged where the decision falls), or failed with its cause.
    // Without it a faulted watch went quiet until disposal rethrew the fault.
    private static async Task SuperviseAsync(Func<CancellationToken, Task> body, CancellationToken ct)
    {
        try
        {
            await body(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Deckle is leaving. The process is intentionally left alive.
            DeckleAnytypeSource.Log.BackendSupervisionStopped();
        }
        catch (Exception ex)
        {
            DeckleAnytypeSource.Log.BackendSupervisionFailed(ex.GetType().Name, ex.Message);
            throw;
        }
    }

    private async Task RecoverAsync(
        string incidentId, int pendingProcessId, long unreadySince, CancellationToken ct)
    {
        (IBackendProcess? process, _) = await RestartAsync(
            BackendTriggers.StartFailed, attempt: 0, incidentId, ct,
            pendingProcessId, unreadySince).ConfigureAwait(false);
        if (process is not null)
            await WatchAsync(process, ct).ConfigureAwait(false);
    }

    private async Task WatchAsync(IBackendProcess process, CancellationToken ct)
    {
        IBackendProcess? current = process;
        int attempt = 0;
        try
        {
            while (current is not null)
            {
                await WaitForExitAsync(current, ct).ConfigureAwait(false);
                // The death opens an incident; its id travels on every line
                // that follows until the backend is back or supervision ends.
                string incidentId = WorkId.Next();
                double uptimeSeconds = BackendProcessReport.Stopped(current, incidentId);
                current.Dispose();
                current = null;

                attempt = uptimeSeconds >= StableUptime.TotalSeconds ? 0 : attempt;
                (current, attempt) = await RestartAsync(
                    BackendTriggers.ProcessExit, attempt, incidentId, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            current?.Dispose();
        }
    }

    // Reconciles on the backoff ladder until a serve runs again. Returns no
    // process when restarting is pointless (not provisioned, endpoint conflict).
    // A reconciliation that throws counts as one failed attempt: before, it
    // ended supervision for the rest of the session without a word. The
    // trigger names what opened the incident, an exit or a start that never
    // served: labelling both "process-exit" hid a live serve stuck in login.
    //
    // This loop is also where the memory across attempts lives, because each
    // reconciliation is a memoryless transaction. It follows the live serve that
    // consecutive timed-out waits name; once that same pid has stayed unready for
    // WedgedServeGrace, the next reconciliation is asked to replace it, once per
    // pid: after a refusal (an elevated serve) asking again would only repeat it.
    // The streak starts over when the pid changes or a result is not a timed-out
    // wait on a live serve. pendingProcessId / unreadySince seed it with the
    // startup wait that opened the incident.
    private async Task<(IBackendProcess? Process, int Attempt)> RestartAsync(
        string trigger, int attempt, string incidentId, CancellationToken ct,
        int pendingProcessId = 0, long unreadySince = 0)
    {
        long downSince = _time.GetTimestamp();
        int failures = 0;
        int unreadyPid = pendingProcessId;
        var asked = new HashSet<int>();
        while (true)
        {
            TimeSpan delay = RestartBackoff[Math.Min(attempt, RestartBackoff.Length - 1)];
            DeckleAnytypeSource.Log.BackendRestartScheduled(attempt + 1, delay.TotalMilliseconds);
            await Task.Run(() => _time.Delay(delay, ct), ct).ConfigureAwait(false);

            // Judged after the backoff, which is part of the time the serve stayed unready.
            int replacePid = 0;
            double unreadySeconds = 0;
            if (unreadyPid != 0 && !asked.Contains(unreadyPid))
            {
                TimeSpan unready = _time.GetElapsedTime(unreadySince);
                if (unready >= WedgedServeGrace)
                {
                    replacePid = unreadyPid;
                    unreadySeconds = unready.TotalSeconds;
                    asked.Add(replacePid);
                }
            }

            long attemptStarted = _time.GetTimestamp();
            BackendReconciliationResult? result = null;
            string outcome;
            string exType = "";
            string exMessage = "";
            try
            {
                result = await _reconciler
                    .ReconcileAsync(trigger, ct, replacePid)
                    .ConfigureAwait(false);
                outcome = result.Decision;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                outcome = "exception";
                exType = ex.GetType().Name;
                exMessage = ex.Message;
            }
            attempt = Math.Min(attempt + 1, RestartBackoff.Length - 1);

            // Written before the recovery below: the serve that comes back is
            // the replacement of the one just ended.
            switch (result?.Replacement)
            {
                case { Outcome: BackendReplaceOutcome.Ended }:
                    DeckleAnytypeSource.Log.BackendWedgedServeEnded(replacePid, unreadySeconds, incidentId);
                    break;
                case { Outcome: BackendReplaceOutcome.Refused } refused:
                    DeckleAnytypeSource.Log.BackendWedgedServeEndRefused(
                        replacePid, refused.ExType, refused.ExMessage, incidentId);
                    break;
            }

            // A thrown reconciliation says nothing about the serve: the streak stays.
            if (result is not null)
            {
                int pending = result.Outcome is BackendStartOutcome.TimedOut ? result.PendingProcessId : 0;
                if (pending != unreadyPid)
                    unreadySince = attemptStarted;
                unreadyPid = pending;
            }

            if (result?.Process is not null)
            {
                DeckleAnytypeSource.Log.BackendRecovered(
                    failures + 1, _time.GetElapsedTime(downSince).TotalSeconds, incidentId);
                CloseUserIncident();
                return (result.Process, attempt);
            }
            if (result?.Outcome is BackendStartOutcome.NotProvisioned)
            {
                DeckleAnytypeSource.Log.BackendSupervisionGaveUp("not_provisioned", incidentId);
                OpenUserIncident("not_provisioned");
                return (null, attempt);
            }
            if (result?.Outcome is BackendStartOutcome.EndpointConflict)
            {
                DeckleAnytypeSource.Log.BackendSupervisionGaveUp("endpoint_conflict", incidentId);
                OpenUserIncident("endpoint_conflict");
                return (null, attempt);
            }

            failures++;
            DeckleAnytypeSource.Log.BackendRestartAttemptFailed(failures, outcome, exType, exMessage, incidentId);
            if (failures == RestartsFailingThreshold)
            {
                DeckleAnytypeSource.Log.BackendRestartsFailing(failures, outcome, exType, exMessage, incidentId);
                OpenUserIncident("restarts_failing");
            }
        }
    }

    private async Task WaitForExitAsync(IBackendProcess process, CancellationToken ct)
    {
        using var exited = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task monitor = MonitorHealthAsync(exited.Token);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            exited.Cancel();
            await monitor.ConfigureAwait(false);
        }
    }

    // One incident per streak: the Warning opens at the threshold, the
    // Information closes it. A streak still open when the process exits is
    // closed by BackendStopped instead.
    private async Task MonitorHealthAsync(CancellationToken ct)
    {
        int failures = 0;
        long failingSince = 0;
        // A probe is due every 30 s for as long as this watch runs: two quiet
        // rollup windows in a row mean the watch itself is stuck, which no
        // BackendHealthProbed line could ever say.
        using IDisposable armed = HotLoops.AnytypeHealthProbe.Arm();
        try
        {
            while (true)
            {
                await Task.Delay(HealthInterval, ct).ConfigureAwait(false);
                BackendHealth health;
                try
                {
                    health = await _health.ProbeAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DeckleAnytypeSource.Log.BackendHealthProbeFailed(ex.GetType().Name, ex.Message);
                    health = new BackendHealth(false, "exception");
                }

                if (health.Healthy)
                {
                    if (failures >= UnresponsiveThreshold)
                    {
                        DeckleAnytypeSource.Log.BackendResponsive(
                            failures, Stopwatch.GetElapsedTime(failingSince).TotalSeconds);
                        CloseUserIncident();
                    }
                    failures = 0;
                    continue;
                }

                if (failures++ == 0) failingSince = Stopwatch.GetTimestamp();
                if (failures == UnresponsiveThreshold)
                {
                    DeckleAnytypeSource.Log.BackendUnresponsive(failures, health.Reason);
                    OpenUserIncident("unresponsive");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The process exited or Deckle is leaving; the watch reports which.
        }
    }

    // The tray shows what the log's incidents mean for the person: the
    // backend is down and Deckle cannot bring it back on its own. It opens
    // where the log marks that point (a failing streak, an unresponsive
    // serve, a give-up, a port held by another program) and closes on the
    // recovery lines. A serve that exits while unresponsive keeps it open
    // until the restart that follows recovers. A first start that finds no
    // backend installed opens nothing: the person may not use Anytype at all.
    private static void OpenUserIncident(string reason)
        => UserIncidentRegistry.Instance.Open(UserIncidents.AnytypeBackend, reason);

    private static void CloseUserIncident()
        => UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);

    private static async Task IgnoreCancellation(Task? task)
    {
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
