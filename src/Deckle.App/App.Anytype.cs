using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Security;

namespace Deckle.App;

// Anytype backend composition. Deckle supervises the headless backend without
// owning it: at boot, make sure the (triggerless) scheduled task is enrolled,
// then probe-and-start through the supervisor. The backend process outlives
// Deckle by construction — parented to the Task Scheduler service, stopped only
// by an explicit act, never by an app rebuild.
public partial class App
{
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

        var task = new BackendScheduledTask();

        // Enrolling the task is a provisioning gesture — the wizard's,
        // eventually. Until the wizard exists, boot enrolls it when absent so
        // the chain lives on any machine that has the binary; IsRegistered
        // guards the common path down to a single schtasks query.
        if (!task.IsRegistered())
            task.EnsureRegistered(BackendInstallation.ServeSpec());

        // Outcomes are observed, not acted on: the supervisor logs its own
        // milestones (starting / ready / timed out), and the surfaces that will
        // show last-known state (wizard, General page) read those. No retry
        // loop here — a failed start is a state the user must see, not a
        // condition to poll against.
        // ConfigureAwait(false) is load-bearing here: OnLaunched dispatched this
        // method on the UI thread, and everything past this await — DPAPI vault
        // reads, the WM_SETTINGCHANGE broadcast in MaterializeEnvironmentVariables,
        // the listener bind — is blocking work that touches no UI state. Without
        // it the whole composition tail would resume on the DispatcherQueue.
        using var health = new BackendHealthProbe();
        await new BackendSupervisor(task, health).EnsureRunningAsync().ConfigureAwait(false);

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
        var tokens = new McpClientTokens(SecretVault.CreateDefault());
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
    // whole quit. The client is disposed after the host that borrows it. Both
    // steps swallow so shutdown, which runs best-effort under QuitApp's
    // try/catch-per-step, never throws from here.
    private void ShutdownAnytypeMcp()
    {
        try { _mcpHost?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); } catch { }
        try { _anytypeApi?.Dispose(); } catch { }
    }
}
