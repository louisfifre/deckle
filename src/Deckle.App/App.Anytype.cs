using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Deckle.Security;
using Deckle.Travel;

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

        try
        {
            // A legacy provider is copied and atomically activated outside the
            // application payload without disturbing the already-running image.
            await BackendInstallation.PrepareAsync(ct).ConfigureAwait(false);

            supervisor = new BackendSupervisor(new BackendHealthProbe());
            BackendStartOutcome outcome = await supervisor
                .EnsureRunningAsync(ct)
                .ConfigureAwait(false);
            if (outcome == BackendStartOutcome.EndpointConflict)
                return;
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
                return;
            }

            IReadOnlyList<McpClientProfile> clients =
                [.. McpClients.All, HomeMcp.Client, TravelMcp.Client];
            var tokens = new McpClientTokens(SecretVault.CreateDefault(), clients);
            try
            {
                tokens.EnsureMinted();
                tokens.MaterializeEnvironmentVariables();
            }
            catch (SecretVaultException)
            {
                DeckleAnytypeMcpSource.Log.HostNotProvisioned();
                return;
            }

            ct.ThrowIfCancellationRequested();
            host = new McpHttpHost(api, tokens);
            hostStarted = host.Start();
            if (!hostStarted) return;

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
        }
        finally
        {
            try
            {
                if (hostStarted && host is not null)
                {
                    using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { hostDrained = await host.StopAsync(drainCts.Token).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        hostDrained = false;
                        DeckleAnytypeSource.Log.AnytypeRuntimeFailed();
                        DeckleAnytypeSource.Log.AnytypeRuntimeFailedDetail(
                            $"MCP drain failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // The API may be disposed only after every MCP request that borrows
                // it is gone. On a failed drain both stay alive until process exit.
                if (hostDrained)
                {
                    if (host is not null)
                    {
                        try { await host.DisposeAsync().ConfigureAwait(false); } catch { }
                    }
                    try { api?.Dispose(); } catch { }
                }
            }
            finally
            {
                // Resident ownership may be released only after the supervisor
                // has stopped every acquisition and watch path, even when the
                // HTTP host itself failed to drain.
                if (supervisor is not null)
                {
                    try { await supervisor.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }

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

        try { cts?.Cancel(); } catch { }
        try { runtime?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        finally { cts?.Dispose(); }
    }
}
