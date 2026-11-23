namespace Deckle.Shell;

// What the "Start elevated" switch could not do, or found stale, for the
// General page to explain. None is the normal state.
public enum ElevatedStartupIssue
{
    None,

    // The person refused the UAC prompt: a choice, not a fault.
    UacDeclined,

    // schtasks failed, or the program path was unknown.
    TaskFailed,

    // A task named Deckle exists but starts another exe (the install moved).
    // Turning the switch on overwrites it.
    PointsElsewhere,
}
