using System.Diagnostics;
using System.IO;

namespace Deckle.Anytype;

// The outcome of an EnsureRunning attempt, surfaced to the caller (the wizard
// and the General page show the last-known state from this).
public enum BackendStartOutcome
{
    AlreadyRunning, // the health probe answered 200 on entry — adopted, not started
    Started,        // a serve was spawned and became healthy in time
    NotProvisioned, // no backend binary on disk — provisioning has not run
    StartRejected,  // the spawn itself failed (logged with detail)
    TimedOut,       // a serve was spawned but health did not come up before the cap
}

// ── BackendSupervisor ────────────────────────────────────────────────────────
//
// The lifecycle owner: "make sure the Anytype backend is running, keep it
// running, and tell me how the first attempt went". It spawns (or adopts) the
// serve through BackendProcess, then holds the process handle and waits on its
// exit — the native event-driven death signal, no polling — restarting on a
// capped backoff for as long as the supervisor lives.
//
// Supervision is bounded by Deckle's lifetime; the serve is not. Dispose stops
// watching and never kills the child, so the backend keeps serving across app
// rebuilds; the next boot re-adopts it through the health probe. The gap this
// accepts: a serve that dies when no Deckle runs stays down until the next
// launch — the door it backs (the MCP host) is down with the app anyway.
//
// The readiness wait is a bounded poll, not a background poller: there is no
// Windows event that fires when the REST listener finishes binding, so after a
// start we poll the health endpoint at a fixed interval until it answers or the
// cap elapses. The loop is one-shot and self-terminating.
public sealed class BackendSupervisor : IDisposable
{
    private readonly BackendProcessSpec _spec;
    private readonly BackendHealthProbe _health;
    private readonly CancellationTokenSource _watchCts = new();
    private Task? _watchTask;

    // How long to wait for the backend to bind after starting it, and how often
    // to re-probe within that window. The backend boots anytype-cli and logs the
    // account in before the REST listener comes up, so allow several seconds.
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeInterval    = TimeSpan.FromMilliseconds(500);

    // The restart ladder: quick first retries for a transient death, capped so a
    // crash-looping serve cannot burn the machine. An instance that stayed up
    // past StableUptime earns a reset — its next death starts the ladder over.
    private static readonly TimeSpan[] RestartBackoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];
    private static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(5);

    public BackendSupervisor(BackendProcessSpec spec, BackendHealthProbe health)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(health);
        _spec   = spec;
        _health = health;
    }

    public async Task<BackendStartOutcome> EnsureRunningAsync(CancellationToken ct = default)
    {
        // No binary: a configuration state, not a failure to retry. Checked
        // before any probe so the answer is deterministic and cheap.
        if (!File.Exists(_spec.ExecutablePath))
        {
            DeckleAnytypeSource.Log.BackendNotProvisioned();
            return BackendStartOutcome.NotProvisioned;
        }

        // Already serving: the common warm path — adopt the live process so its
        // death is watched too. A serve that is healthy but unfindable (module
        // path unreadable) is left unwatched rather than blocking the boot.
        if (await _health.IsHealthyAsync(ct).ConfigureAwait(false))
        {
            var adopted = BackendProcess.TryFindRunning(_spec.ExecutablePath);
            if (adopted is not null)
            {
                DeckleAnytypeSource.Log.BackendProcessAttached(adopted.Id, "adopted");
                StartWatching(adopted);
            }
            return BackendStartOutcome.AlreadyRunning;
        }

        DeckleAnytypeSource.Log.BackendStarting();
        var process = BackendProcess.Spawn(_spec);
        if (process is null)
            return BackendStartOutcome.StartRejected;

        DeckleAnytypeSource.Log.BackendProcessAttached(process.Id, "spawned");

        // Watch regardless of the readiness verdict: a serve that timed out but
        // binds late is still supervised; one that dies gets restarted.
        var outcome = await WaitUntilHealthyAsync(ct).ConfigureAwait(false);
        StartWatching(process);
        return outcome;
    }

    // Stops supervising. Never kills the serve — outliving Deckle is the point.
    public void Dispose()
    {
        _watchCts.Cancel();
        try { _watchTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _watchCts.Dispose();
        _health.Dispose();
    }

    private void StartWatching(Process process) =>
        _watchTask = WatchAsync(process, _watchCts.Token);

    // The supervision loop: wait for the current serve to exit (an OS wait on
    // the process handle, zero cost while it lives), then respawn on the
    // backoff ladder. Cancellation — Deckle quitting — exits the loop leaving
    // the serve alive. Owns the Process objects it holds.
    private async Task WatchAsync(Process process, CancellationToken ct)
    {
        var current = process;
        int attempt = 0;
        try
        {
            while (true)
            {
                long startTicks = Stopwatch.GetTimestamp();
                await current.WaitForExitAsync(ct).ConfigureAwait(false);

                TimeSpan uptime = Stopwatch.GetElapsedTime(startTicks);
                DeckleAnytypeSource.Log.BackendStopped();
                DeckleAnytypeSource.Log.BackendStoppedDetail(
                    current.Id, SafeExitCode(current), uptime.TotalSeconds);

                // A long-lived instance restarts the ladder; a quick death
                // climbs it. The uptime measured here starts at watch time, not
                // process start — close enough for the stability question.
                attempt = uptime >= StableUptime ? 0 : Math.Min(attempt + 1, RestartBackoff.Length - 1);

                current.Dispose();
                current = await RespawnAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown: stop watching, leave the serve (if any) running.
        }
        finally
        {
            current?.Dispose();
        }

        // Climbs the ladder on each failed spawn attempt; only returns with a
        // live process.
        async Task<Process> RespawnAsync(CancellationToken ct)
        {
            while (true)
            {
                await Task.Delay(RestartBackoff[Math.Min(attempt, RestartBackoff.Length - 1)], ct)
                          .ConfigureAwait(false);

                DeckleAnytypeSource.Log.BackendStarting();
                var next = BackendProcess.Spawn(_spec);
                if (next is not null)
                {
                    DeckleAnytypeSource.Log.BackendProcessAttached(next.Id, "spawned");
                    await WaitUntilHealthyAsync(ct).ConfigureAwait(false);
                    return next;
                }

                attempt = Math.Min(attempt + 1, RestartBackoff.Length - 1);
            }
        }
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

    // An adopted process can refuse its exit code (handle without query rights,
    // rare); -1 marks "unknown" in the stopped detail rather than throwing
    // inside the watch loop.
    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }
}
