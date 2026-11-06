using Deckle.Anytype.Mcp;
using Deckle.Home;
using Deckle.Security;

namespace Deckle.Anytype.Gateway;

// The gateway's composition: credentials, client tokens, the delete approval
// relay and the MCP host, then a wait until the supervisor asks it to stop.
// Formerly the second half of Deckle.App's Anytype runtime; the first half,
// supervising the anytype-cli backend, stays in Deckle (ADR-0008).
internal static class GatewayRuntime
{
    // The host drains active tool calls for at most this long before exiting.
    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(5);

    public static (int ExitCode, string Outcome) Run()
    {
        // The key (vault entry `anytype-api-key`) is written by a script outside
        // the repository and read here once, at start. A newly minted key
        // therefore needs a gateway restart: rebuild the gateway, or end its
        // process and let Deckle start a fresh one.
        AnytypeApiClient api;
        try
        {
            api = AnytypeApiClient.CreateForResidentGateway(AnytypeCredentials.Load());
        }
        catch (Exception ex) when (ex is InvalidOperationException or SecretVaultException or UriFormatException)
        {
            // InvalidOperationException is AnytypeCredentials.Load's refusal:
            // the credentials file is absent, unreadable or incomplete.
            string reason = ex switch
            {
                SecretVaultException => "vault_error",
                UriFormatException   => "bad_endpoint",
                _                    => "credentials_missing",
            };
            DeckleAnytypeMcpSource.Log.HostProvisioningFailed(reason, ex.GetType().Name, ex.Message);
            return (GatewayContract.ExitCodes.NotProvisioned, "not_provisioned");
        }

        McpHttpHost? host = null;
        bool drained = true;
        try
        {
            IReadOnlyList<McpClientProfile> clients = [.. McpClients.All, HomeMcp.Client];
            var tokens = new McpClientTokens(SecretVault.CreateDefault(), clients);
            try
            {
                tokens.EnsureMinted();
                tokens.MaterializeEnvironmentVariables();
            }
            catch (SecretVaultException ex)
            {
                DeckleAnytypeMcpSource.Log.HostProvisioningFailed("token_mint_failed", ex.GetType().Name, ex.Message);
                return (GatewayContract.ExitCodes.NotProvisioned, "not_provisioned");
            }

            // Only the resident app can show the toast the delete waits on.
            DeletionApproval.Current = new PipeDeletionApproval(GatewayContract.DeletionApprovalPipeName);

            // Created before the host listens, so the supervisor that sees the
            // listener can always reach the stop event.
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset,
                GatewayContract.StopEventName(Environment.ProcessId));
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                // Also raised after Run returned, once the handle is disposed.
                try { stop.Set(); }
                catch (ObjectDisposedException) { }
            };

            host = new McpHttpHost(api, tokens);
            if (!host.Start())
                return (GatewayContract.ExitCodes.ListenFailed, "listen_failed");

            stop.WaitOne();

            using var drain = new CancellationTokenSource(DrainBudget);
            drained = host.StopAsync(drain.Token).GetAwaiter().GetResult();
            return (GatewayContract.ExitCodes.Stopped, "stopped");
        }
        finally
        {
            // The API may be disposed only after every request that borrows it
            // is gone; on a failed drain process exit finishes the teardown.
            if (drained)
            {
                host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                api.Dispose();
            }
        }
    }
}
