using Deckle.Anytype;

namespace Deckle.App;

// Anytype backend composition. Deckle supervises the headless backend without
// owning it: at boot, make sure the (triggerless) scheduled task is enrolled,
// then probe-and-start through the supervisor. The backend process outlives
// Deckle by construction — parented to the Task Scheduler service, stopped only
// by an explicit act, never by an app rebuild.
public partial class App
{
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
        using var health = new BackendHealthProbe();
        await new BackendSupervisor(task, health).EnsureRunningAsync();
    }
}
