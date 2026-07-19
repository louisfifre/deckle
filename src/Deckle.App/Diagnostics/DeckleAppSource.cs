using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// Host App provider. Covers App.xaml.cs (process lifecycle, status, hotkey,
// ambient observers, shutdown/restart, command-line, post-build, HUD wiring),
// as well as HUD surfaces (HudWindow / HudOverlayManager) and LogWindow
// itself. Anything that does not fit in a specific module but lives in the
// host app goes through this provider.
//
// Provider Name = "Deckle-App" → tag [APP] via LogLineFormatter. Le
// legacy used both [APP] and [STATUS] for different host emissions; [STATUS]
// is kept as an event name but the tag stays [APP]. The ".App" suffix is
// intentional: Diagnostics listeners observe the canonical Deckle.* family.
[EventSource(Name = "Deckle-App")]
public sealed partial class DeckleAppSource : DeckleEventSource
{
    public static readonly DeckleAppSource Log = new();

    private DeckleAppSource() { }

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtCrashUnhandled            = 1;
    public const int EvtCrashAppDomain            = 2;
    public const int EvtCrashTaskScheduler        = 3;
    public const int EvtCrashStackTrace           = 4;
    public const int EvtProcessExit               = 5;
    public const int EvtPathsInitialized          = 6;
    public const int EvtPathsDetail               = 7;
    public const int EvtStatusChanged             = 8;
    public const int EvtStartupMilestones         = 9;
    public const int EvtShutdownRequested         = 10;
    public const int EvtShutdownWarning           = 11;
    public const int EvtRestartRequested          = 12;
    public const int EvtRestartFromTrayRequested  = 13;
    public const int EvtRestartSpawnNewProcess    = 14;
    public const int EvtPostBuildRestartRequested = 15;
    public const int EvtPostBuildShellExecute     = 16;
    public const int EvtPostBuildRelaunchFailed   = 17;
    public const int EvtCmdLineSettingsFlag       = 18;
    public const int EvtCmdLinePostBuildFlag      = 19;
    public const int EvtAmbientPipelineState      = 20;
    public const int EvtAmbientMasterForcedOff    = 21;
    public const int EvtAmbientStartFailed        = 22;
    public const int EvtHotkeyStart               = 23;
    public const int EvtHotkeyStop                = 24;
    public const int EvtHotkeyNoProfile           = 25;
    public const int EvtHudWarning                = 26;
    public const int EvtLogWindowWarning          = 27;
    public const int EvtUserFeedbackEmitted       = 28;
    // Verbose mirrors appended for the Verbose/Info separation: each milestone
    // above whose message carried a placeholder (exception, status text,
    // forwarded warning line) now emits a short Capital sentence, and the
    // technical detail moves to one of these fresh ids. IDs are public in the
    // ETW manifest; never reuse an id.
    public const int EvtCrashUnhandledDetail        = 29;
    public const int EvtCrashAppDomainDetail        = 30;
    public const int EvtCrashTaskSchedulerDetail    = 31;
    public const int EvtStatusChangedDetail         = 32;
    public const int EvtShutdownWarningDetail       = 33;
    public const int EvtPostBuildRelaunchFailedDetail = 34;
    public const int EvtAmbientPipelineStateDetail  = 35;
    public const int EvtAmbientStartFailedDetail    = 36;
    public const int EvtHotkeyStartDetail           = 37;
    public const int EvtHotkeyNoProfileDetail       = 38;
    public const int EvtHudWarningDetail            = 39;
    public const int EvtLogWindowWarningDetail      = 40;
    // Install mode (the wizard as installer).
    public const int EvtInstallModeEntered          = 41;
    public const int EvtInstallModeEnteredDetail    = 42;
    public const int EvtAppStarting                 = 43;
    public const int EvtAppReady                    = 44;
    public const int EvtShutdownCompleted           = 45;

}
