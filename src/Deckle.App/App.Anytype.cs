using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Deckle.Security;

namespace Deckle.App;

// Anytype backend composition. Deckle spawns the headless backend windowless
// and supervises it for as long as the app lives: watch the process handle,
// restart on death with a capped backoff. The serve still outlives Deckle — a
// child process is not tied to its parent's lifetime, and quitting only stops
// the watching, never the serve — so app rebuilds keep their warm backend and
// the next boot re-adopts it.
public partial class App
{
    // The lifecycle owner of the serve process: spawn/adopt at boot, restart on
    // death. Owned here so quitting stops the supervision (not the serve).
    private Deckle.Anytype.BackendSupervisor? _backendSupervisor;

    // The REST client backing the MCP host and, through it, the tool calls that
    // reach the local Anytype API. Owned here so shutdown disposes it after the
    // host that uses it.
    private Deckle.Anytype.AnytypeApiClient? _anytypeApi;

    // The resident MCP HTTP host — the door external AI clients (Claude, Codex)
    // knock on to reach the space. Composed once at boot, torn down at quit.
    private Deckle.Anytype.Mcp.McpHttpHost? _mcpHost;

    // Fire-and-forget from OnLaunched: REST binds in its own time (the
    // supervisor's bounded readiness poll), and nothing at boot waits on the
    // backend. Same posture as ApplyAmbientEnabledAsync.
    private async Task InitializeAnytypeBackendAsync()
    {
        // No binary on disk → the module stays dormant, the same posture as the
        // speech provisioning gate: never a hard stop at boot. Downloading the
        // pinned binary is the wizard's act (to come); until then it is a
        // maintainer act.
        if (!BackendInstallation.IsInstalled())
        {
            DeckleAnytypeSource.Log.BackendNotProvisioned();
            return;
        }

        // Outcomes are observed, not acted on: the supervisor logs its own
        // milestones (starting / ready / stopped / timed out), and the surfaces
        // that will show last-known state (wizard, General page) read those.
        // Mid-session recovery is the supervisor's own restart ladder, driven
        // by the process-exit signal — no polling here.
        // ConfigureAwait(false) is load-bearing here: OnLaunched dispatched this
        // method on the UI thread, and everything past this await — DPAPI vault
        // reads, the WM_SETTINGCHANGE broadcast in MaterializeEnvironmentVariables,
        // the listener bind — is blocking work that touches no UI state. Without
        // it the whole composition tail would resume on the DispatcherQueue.
        _backendSupervisor = new BackendSupervisor(
            BackendInstallation.ServeSpec(), new BackendHealthProbe());
        await _backendSupervisor.EnsureRunningAsync().ConfigureAwait(false);

        // The host comes up whatever the supervisor concluded: a backend that
        // failed to start surfaces as isError tool results the client can read,
        // never as a door that refuses to exist. The one thing that keeps the
        // door shut is an unprovisioned machine — no credentials to bind a
        // client to (or a credential store that cannot be read), the same gate
        // the backend block above applies to a missing binary. Caught broadly on
        // purpose: this Task is discarded by OnLaunched, so anything escaping
        // here would die unobserved.
        AnytypeCredentials credentials;
        try
        {
            credentials = AnytypeCredentials.Load();
            _anytypeApi = new AnytypeApiClient(credentials);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SecretVaultException or UriFormatException)
        {
            DeckleAnytypeMcpSource.Log.HostNotProvisioned();
            return;
        }

        // Minting and materializing the client tokens is idempotent, so folding
        // it into boot means a provisioned machine needs no separate act to make
        // the host reachable. Rewiring the AI clients themselves (claude/codex
        // mcp add against these tokens) stays a wizard/maintainer gesture — the
        // app hands out the credential, it does not reconfigure someone else's
        // client. A vault that cannot be written is the same dormant-door state
        // as one that cannot be read above.
        // Core Anytype clients and optional domain surfaces meet only at the
        // application composition root. Selecting the Anytype module installs
        // this build's Home adapter and provisions its bearer alongside the
        // reusable Anytype surfaces; later UI can choose a narrower list here.
        IReadOnlyList<McpClientProfile> clients =
            [.. McpClients.All, HomeMcp.Client];
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

        // A failed bind (port taken) already logged its Warning inside Start;
        // dropping the reference keeps shutdown honest — there is nothing to
        // tear down, and no field implying an open door that never opened.
        _mcpHost = new McpHttpHost(_anytypeApi, tokens);
        if (!_mcpHost.Start())
            _mcpHost = null;
    }

    // Bound the host teardown the same way QuitApp bounds the ambient engine: a
    // five-second cap on the async dispose so a wedged listener cannot hang the
    // whole quit. The client is disposed after the host that borrows it, and
    // the supervisor last — disposing it stops the watching, never the serve.
    // Every step swallows so shutdown, which runs best-effort under QuitApp's
    // try/catch-per-step, never throws from here.
    private void ShutdownAnytypeMcp()
    {
        try { _mcpHost?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); } catch { }
        try { _anytypeApi?.Dispose(); } catch { }
        try { _backendSupervisor?.Dispose(); } catch { }
    }
}
