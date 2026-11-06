using Deckle.Anytype;

namespace Deckle.App;

public partial class App
{
    private CancellationTokenSource? _anytypeLifetimeCts;
    private Task? _anytypeRuntimeTask;

    // Captures the whole Anytype runtime as one owned task. Shutdown cancels and
    // drains this task before resident ownership is released, so no outgoing
    // Deckle can acquire or spawn a backend or a gateway after its successor
    // starts.
    private void StartAnytypeRuntime()
    {
        var cts = new CancellationTokenSource();
        _anytypeLifetimeCts = cts;
        _anytypeRuntimeTask = RunAnytypeRuntimeAsync(cts.Token);
    }

    // Deckle's half of the Anytype runtime (ADR-0008): it supervises the
    // anytype-cli backend and the Anytype gateway, two processes that both
    // outlive it, and answers the gateway's delete approvals with a toast. The
    // MCP host itself runs in the gateway, so assistants keep their access while
    // Deckle relaunches or rebuilds.
    private async Task RunAnytypeRuntimeAsync(CancellationToken ct)
    {
        BackendSupervisor? supervisor = null;
        GatewaySupervisor? gateway = null;
        DeletionApprovalPipeServer? approvals = null;

        // The one end line of the resident runtime. The owning providers log
        // each cause in detail; this line says why the runtime is gone.
        string endOutcome = "cancelled";
        string endReason = "shutdown";
        string endExType = "";
        string endExMessage = "";

        // Which composition step is running, so an unexpected exception names
        // the phase AnytypeRuntimeFaulted's vocabulary documents.
        string phase = "prepare";

        try
        {
            // Listening first: a gateway adopted from an earlier Deckle may ask
            // for an approval as soon as this Deckle is up.
            phase = "approvals";
            approvals = new DeletionApprovalPipeServer(
                GatewayContract.DeletionApprovalPipeName, DeletionApproval.Current);
            approvals.Start();

            phase = "prepare";
            await BackendInstallation.PrepareAsync(ct).ConfigureAwait(false);

            phase = "supervise";
            supervisor = new BackendSupervisor(new BackendHealthProbe());
            BackendStartOutcome outcome = await supervisor
                .EnsureRunningAsync(ct)
                .ConfigureAwait(false);
            if (outcome == BackendStartOutcome.EndpointConflict)
            {
                (endOutcome, endReason) = ("failed", "endpoint_conflict");
                return;
            }
            ct.ThrowIfCancellationRequested();

            // The gateway reads its own credentials and tokens; a gateway that
            // finds them missing exits and the supervisor leaves it stopped.
            phase = "gateway";
            gateway = new GatewaySupervisor(
                Path.Combine(AppContext.BaseDirectory, GatewayContract.PayloadFolderName));
            gateway.Start();

            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal Deckle shutdown. Cleanup below drains in dependency order.
        }
        catch (Exception ex)
        {
            // This task is resident and intentionally not awaited during boot.
            // Observe unexpected composition failures here, when they happen,
            // rather than leaving them latent until application shutdown.
            DeckleAnytypeSource.Log.AnytypeRuntimeFaulted(phase, ex.GetType().Name, ex.Message);
            (endOutcome, endReason) = ("failed", "exception");
            (endExType, endExMessage) = (ex.GetType().Name, ex.Message);
        }
        finally
        {
            // Supervision only: the gateway and the backend keep running.
            // Resident ownership may be released only after both supervisors
            // have stopped every acquisition, spawn and watch path.
            if (gateway is not null)
            {
                try { await gateway.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    DeckleAppSource.Log.CleanupFailed("anytype_gateway_supervisor", ex.GetType().Name, ex.Message);
                }
            }

            if (supervisor is not null)
            {
                try { await supervisor.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    DeckleAppSource.Log.CleanupFailed("anytype_supervisor", ex.GetType().Name, ex.Message);
                    // A faulted watch task rethrows here: supervision died
                    // during the run, which the end line must say when no
                    // earlier cause already explains the end.
                    if (endReason == "shutdown")
                    {
                        (endOutcome, endReason) = ("failed", "supervisor_faulted");
                        (endExType, endExMessage) = (ex.GetType().Name, ex.Message);
                    }
                }
            }

            // Last: an approval the gateway asked during shutdown still gets an
            // answer, or a clean Unavailable once the pipe is gone.
            if (approvals is not null)
            {
                try { await approvals.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    DeckleAppSource.Log.CleanupFailed("anytype_delete_approvals", ex.GetType().Name, ex.Message);
                }
            }

            // A stop on a failure is an Error; a shutdown is only an end.
            if (endOutcome == "failed")
                DeckleAppSource.Log.AnytypeRuntimeFailed(endReason, endExType, endExMessage);
            else
                DeckleAppSource.Log.AnytypeRuntimeEnded(endOutcome, endReason, endExType, endExMessage);
        }
    }

    private void ShutdownAnytypeMcp()
    {
        CancellationTokenSource? cts = _anytypeLifetimeCts;
        Task? runtime = _anytypeRuntimeTask;
        _anytypeLifetimeCts = null;
        _anytypeRuntimeTask = null;

        try { cts?.Cancel(); }
        catch (Exception ex)
        {
            DeckleAppSource.Log.CleanupFailed("anytype_cancel", ex.GetType().Name, ex.Message);
        }
        try { runtime?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        finally { cts?.Dispose(); }
    }
}
