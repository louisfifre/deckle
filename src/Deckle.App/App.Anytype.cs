using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Deckle.Security;

namespace Deckle.App;

public partial class App
{
    private CancellationTokenSource? _anytypeLifetimeCts;
    private Task? _anytypeRuntimeTask;

    // Captures the whole Anytype runtime as one owned task. Shutdown cancels and
    // drains this task before resident ownership is released, so no outgoing
    // Deckle can acquire or spawn a backend after its successor starts.
    private void StartAnytypeRuntime()
    {
        var cts = new CancellationTokenSource();
        _anytypeLifetimeCts = cts;
        _anytypeRuntimeTask = RunAnytypeRuntimeAsync(cts.Token);
    }

    private async Task RunAnytypeRuntimeAsync(CancellationToken ct)
    {
        BackendSupervisor? supervisor = null;
        AnytypeApiClient? api = null;
        McpHttpHost? host = null;
        bool hostStarted = false;
        bool hostDrained = true;

        // The one end line of the resident runtime. The owning providers log
        // each cause in detail; this line says why the runtime is gone.
        string endOutcome = "cancelled";
        string endReason = "shutdown";
        string endExType = "";
        string endExMessage = "";

        try
        {
            await BackendInstallation.PrepareAsync(ct).ConfigureAwait(false);

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

            AnytypeCredentials credentials;
            try
            {
                credentials = AnytypeCredentials.Load();
                api = AnytypeApiClient.CreateForResidentGateway(credentials);
            }
            catch (Exception ex) when (ex is InvalidOperationException or SecretVaultException or UriFormatException)
            {
                DeckleAnytypeMcpSource.Log.HostNotProvisioned();
                (endOutcome, endReason) = ("skipped", "not_provisioned");
                (endExType, endExMessage) = (ex.GetType().Name, ex.Message);
                return;
            }

            IReadOnlyList<McpClientProfile> clients =
                [.. McpClients.All, HomeMcp.Client];
            var tokens = new McpClientTokens(SecretVault.CreateDefault(), clients);
            try
            {
                tokens.EnsureMinted();
                tokens.MaterializeEnvironmentVariables();
            }
            catch (SecretVaultException ex)
            {
                DeckleAnytypeMcpSource.Log.HostNotProvisioned();
                (endOutcome, endReason) = ("skipped", "not_provisioned");
                (endExType, endExMessage) = (ex.GetType().Name, ex.Message);
                return;
            }

            ct.ThrowIfCancellationRequested();
            host = new McpHttpHost(api, tokens);
            hostStarted = host.Start();
            if (!hostStarted)
            {
                (endOutcome, endReason) = ("failed", "host_start_failed");
                return;
            }

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
            DeckleAnytypeSource.Log.AnytypeRuntimeFailed();
            DeckleAnytypeSource.Log.AnytypeRuntimeFailedDetail(
                $"{ex.GetType().Name}: {ex.Message}");
            (endOutcome, endReason) = ("failed", "exception");
            (endExType, endExMessage) = (ex.GetType().Name, ex.Message);
        }
        finally
        {
            try
            {
                if (hostStarted && host is not null)
                {
                    long drainStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    string drainFailure = "timeout";
                    using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { hostDrained = await host.StopAsync(drainCts.Token).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        hostDrained = false;
                        drainFailure = "exception";
                        DeckleAnytypeSource.Log.AnytypeRuntimeFailed();
                        DeckleAnytypeSource.Log.AnytypeRuntimeFailedDetail(
                            $"MCP drain failed: {ex.GetType().Name}: {ex.Message}");
                    }
                    // A false return was silent: in-flight MCP requests outlived
                    // the 5 s budget, so the host and the API stay alive to exit.
                    if (!hostDrained)
                    {
                        DeckleAppSource.Log.AnytypeHostDrainIncomplete(
                            drainFailure,
                            (long)System.Diagnostics.Stopwatch.GetElapsedTime(drainStarted).TotalMilliseconds);
                    }
                }

                // The API may be disposed only after every MCP request that borrows
                // it is gone. On a failed drain both stay alive until process exit.
                if (hostDrained)
                {
                    if (host is not null)
                    {
                        try { await host.DisposeAsync().ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            DeckleAppSource.Log.CleanupFailed("anytype_mcp_host", ex.GetType().Name, ex.Message);
                        }
                    }
                    try { api?.Dispose(); }
                    catch (Exception ex)
                    {
                        DeckleAppSource.Log.CleanupFailed("anytype_api", ex.GetType().Name, ex.Message);
                    }
                }
            }
            finally
            {
                // Resident ownership may be released only after the supervisor
                // has stopped every acquisition and watch path, even when the
                // HTTP host itself failed to drain.
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
            }

            DeckleAppSource.Log.AnytypeRuntimeEnded(endOutcome, endReason, endExType, endExMessage);

            GC.KeepAlive(host);
            GC.KeepAlive(api);
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
