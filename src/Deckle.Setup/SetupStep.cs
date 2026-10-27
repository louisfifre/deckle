namespace Deckle.Setup;

// The stops of the wizard as the person sees them in the step rail. Declaration
// order is the display order AND the progress order: the rail compares steps
// with < to tell a completed step from an upcoming one, so a new step goes where
// it happens in the flow. A page maps to exactly one step (SetCurrentStep), but
// a step may span several pages: Install covers both the deploy page and the
// provisioning page.
public enum SetupStep
{
    Modules,
    Location,
    Transcription,
    Install,
    Finish,
}
