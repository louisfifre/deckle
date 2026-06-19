using System.Diagnostics;

namespace Deckle.Anytype;

// The outcome of an EnsureRunning attempt, surfaced to the caller (the wizard
// and the General page show the last-known state from this).
public enum BackendStartOutcome
{
    AlreadyRunning, // the health probe answered 200 on entry — nothing to do
    Started,        // the task was run and the backend became healthy in time
    NotProvisioned, // no backend task is registered — provisioning has not run
    StartRejected,  // schtasks refused the /Run (logged with detail)
    TimedOut,       // the task was run but health did not come up before the cap
}

// ── BackendSupervisor ────────────────────────────────────────────────────────
//
// The lifecycle entry point: "make sure the Anytype backend is running, and tell
// me how it went". It supervises without owning the process — it never holds a
// handle or a PID, only probes health and asks the Task Scheduler to start the
// enrolled task. That indirection is what lets the backend outlive Deckle.
//
// It does NOT provision: registering the task (with the binary path + serve args)
// is a distinct gesture the provisioning step performs once via
// BackendScheduledTask.EnsureRegistered. The supervisor only reads and runs — so
// it needs no BackendProcessSpec. A missing task surfaces as NotProvisioned, not
// as an attempt to enroll one here.
//
// The readiness wait is a bounded poll, not a background poller: there is no
// Windows event that fires when the REST listener finishes binding, so after the
// start we poll the health endpoint at a fixed interval until it answers or the
// cap elapses. The loop is one-shot and self-terminating — it is not a standing
// timer.
public sealed class BackendSupervisor
{
    private readonly BackendScheduledTask _task;
    private readonly BackendHealthProbe _health;

    // How long to wait for the backend to bind after starting it, and how often
    // to re-probe within that window. The backend boots anytype-cli before the
    // REST listener comes up, so allow several seconds.
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeInterval    = TimeSpan.FromMilliseconds(500);

    public BackendSupervisor(BackendScheduledTask task, BackendHealthProbe health)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(health);
        _task   = task;
        _health = health;
    }

    public async Task<BackendStartOutcome> EnsureRunningAsync(CancellationToken ct = default)
    {
        // Already serving: the common warm path, nothing to start.
        if (await _health.IsHealthyAsync(ct).ConfigureAwait(false))
            return BackendStartOutcome.AlreadyRunning;

        // Down: only Deckle can start it, and only if provisioning enrolled the
        // task. Absence is a configuration state, not a failure to retry.
        if (!_task.IsRegistered())
        {
            DeckleAnytypeSource.Log.BackendNotProvisioned();
            return BackendStartOutcome.NotProvisioned;
        }

        DeckleAnytypeSource.Log.BackendStarting();
        if (!_task.Run())
            return BackendStartOutcome.StartRejected;

        return await WaitUntilHealthyAsync(ct).ConfigureAwait(false);
    }

    // Polls health until it answers 200 or the readiness cap elapses.
    private async Task<BackendStartOutcome> WaitUntilHealthyAsync(CancellationToken ct)
    {
        long startTicks = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startTicks) < ReadinessTimeout)
        {
            await Task.Delay(ProbeInterval, ct).ConfigureAwait(false);

            if (await _health.IsHealthyAsync(ct).ConfigureAwait(false))
            {
                DeckleAnytypeSource.Log.BackendReady();
                return BackendStartOutcome.Started;
            }
        }

        DeckleAnytypeSource.Log.BackendStartTimedOut();
        return BackendStartOutcome.TimedOut;
    }
}
