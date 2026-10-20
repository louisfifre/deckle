using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// Provider for what lives in the host app and fits no module, LogWindow included.
// The "Deckle-" prefix is what DispatchEventListener discovers providers by.
[EventSource(Name = "Deckle-App")]
public sealed partial class DeckleAppSource : DeckleEventSource
{
    public static readonly DeckleAppSource Log = new();

    private DeckleAppSource() { }

    // IDs are public in the ETW manifest: never reuse one, retired ids included.
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
    // 25 retired.
    public const int EvtHudWarning                = 26;
    public const int EvtLogWindowWarning          = 27;
    public const int EvtUserFeedbackEmitted       = 28;
    public const int EvtCrashUnhandledDetail        = 29;
    public const int EvtCrashAppDomainDetail        = 30;
    public const int EvtCrashTaskSchedulerDetail    = 31;
    public const int EvtStatusChangedDetail         = 32;
    public const int EvtShutdownWarningDetail       = 33;
    public const int EvtPostBuildRelaunchFailedDetail = 34;
    public const int EvtAmbientPipelineStateDetail  = 35;
    public const int EvtAmbientStartFailedDetail    = 36;
    public const int EvtHotkeyStartDetail           = 37;
    // 38 retired.
    public const int EvtHudWarningDetail            = 39;
    public const int EvtLogWindowWarningDetail      = 40;
    public const int EvtInstallModeEntered          = 41;
    public const int EvtInstallModeEnteredDetail    = 42;
    public const int EvtAppStarting                 = 43;
    public const int EvtAppReady                    = 44;
    public const int EvtShutdownCompleted           = 45;
    public const int EvtHotkeyRegistrationFailed       = 46;
    public const int EvtHotkeyRegistrationFailedDetail = 47;
    public const int EvtHotkeyToggle                   = 48;
    public const int EvtHotkeyToggleDetail             = 49;
    public const int EvtCrashDumpCaptureArmed          = 50;
    public const int EvtCrashDumpCaptureArmedDetail    = 51;
    public const int EvtCrashDumpCaptureOff            = 52;
    public const int EvtCrashDumpCaptureOffDetail      = 53;
    public const int EvtCrashDumpFound                 = 54;
    public const int EvtCrashDumpFoundDetail           = 55;
    public const int EvtDictationRecoveryFound         = 56;
    public const int EvtDictationRecovered             = 57;
    public const int EvtDictationRecoveredDetail       = 58;
    public const int EvtDictationRecoveryUnreadable    = 59;
    public const int EvtDictationRecoveryClipboardFailed = 60;
    public const int EvtCrashRelaunched                = 61;
    public const int EvtCrashRelaunchedDetail          = 62;
    public const int EvtLaunchContextDetail            = 63;

}
