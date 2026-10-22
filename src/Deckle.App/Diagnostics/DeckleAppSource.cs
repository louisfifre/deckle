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
    // 1 retired.
    // 2 retired.
    // 3 retired.
    public const int EvtCrashStackTrace           = 4;
    public const int EvtProcessExit               = 5;
    // 6 retired.
    public const int EvtPathsDetail               = 7;
    // 8 retired.
    public const int EvtStartupMilestones         = 9;
    public const int EvtShutdownRequested         = 10;
    // 11 retired.
    public const int EvtRestartRequested          = 12;
    public const int EvtRestartFromTrayRequested  = 13;
    public const int EvtRestartSpawnNewProcess    = 14;
    public const int EvtPostBuildRestartRequested = 15;
    public const int EvtPostBuildShellExecute     = 16;
    // 17 retired.
    public const int EvtCmdLineSettingsFlag       = 18;
    public const int EvtCmdLinePostBuildFlag      = 19;
    // 20 retired.
    public const int EvtAmbientMasterForcedOff    = 21;
    // 22 retired.
    public const int EvtHotkeyStart               = 23;
    public const int EvtHotkeyStop                = 24;
    // 25 retired.
    // 26 retired.
    // 27 retired.
    public const int EvtUserFeedbackEmitted       = 28;
    public const int EvtCrashUnhandledDetail        = 29;
    public const int EvtCrashAppDomainDetail        = 30;
    public const int EvtCrashTaskSchedulerDetail    = 31;
    // 32 retired.
    // 33 retired.
    // 34 retired.
    // 35 retired.
    // 36 retired.
    public const int EvtHotkeyStartDetail           = 37;
    // 38 retired.
    // 39 retired.
    // 40 retired.
    public const int EvtInstallModeEntered          = 41;
    public const int EvtInstallModeEnteredDetail    = 42;
    public const int EvtAppStarting                 = 43;
    public const int EvtAppReady                    = 44;
    public const int EvtShutdownCompleted           = 45;
    // 46 retired.
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
    // 59 retired.
    // 60 retired.
    // 61 retired.
    // 62 retired.
    public const int EvtRunIdentity                    = 63;
    public const int EvtLoggingPolicy                  = 64;
    public const int EvtModuleStarted                  = 65;
    public const int EvtModuleSkipped                  = 66;
    public const int EvtModuleFailed                   = 67;
    public const int EvtShutdownStepFailed             = 68;
    public const int EvtShutdownStepsDetail            = 69;
    public const int EvtCrashUnhandled                 = 70;
    public const int EvtCrashAppDomain                 = 71;
    public const int EvtCrashTaskScheduler             = 72;
    public const int EvtStatusChanged                  = 73;
    public const int EvtInstallTempCleanupFailed       = 74;
    public const int EvtDataRootCleanupStarting        = 75;
    public const int EvtDataRootCleanupFinished        = 76;
    public const int EvtDataRootCleanupFailed          = 77;
    public const int EvtPostBuildRelaunchFailed        = 78;
    public const int EvtRestartSettingsFlushFailed     = 79;
    public const int EvtRestartSpawnSkipped            = 80;
    public const int EvtRestartSpawned                 = 81;
    public const int EvtHotkeyRegistrationFailed       = 82;
    // 83 retired.
    public const int EvtFileTranscriptionEntryFailed   = 84;
    public const int EvtWindowOperationFailed          = 85;
    public const int EvtCrashRelaunched                = 86;
    public const int EvtDictationRecoveryUnreadable    = 87;
    public const int EvtDictationRecoveryClipboardFailed = 88;
    public const int EvtDictationRecoveryKept          = 89;
    // 90 retired: a rejected second instance has no sink, so the fact is a
    // line in second-instance.txt (App.Lifetime.cs).
    public const int EvtResidentMutexAbandoned         = 91;
    public const int EvtResidentMutexReleaseFailed     = 92;
    public const int EvtTransactionalModeExited        = 93;
    public const int EvtTransactionalModeFailed        = 94;
    public const int EvtUpdateChecksStarted            = 95;
    public const int EvtUpdateCheckOutcome             = 96;
    public const int EvtUpdateChecksStopped            = 97;
    public const int EvtUpdateFlowStarted              = 98;
    public const int EvtUpdateFlowRefused              = 99;
    public const int EvtUpdateFlowFinished             = 100;
    public const int EvtUpdateFlowFailed               = 101;
    public const int EvtAutocorrectUnavailable         = 102;
    public const int EvtAutocorrectFailed              = 103;
    public const int EvtAutocorrectAssetSkipped        = 104;
    public const int EvtAutocorrectEnrollmentPromptFailed = 105;
    public const int EvtCleanupFailed                  = 106;
    public const int EvtInputConsumerUnavailable       = 107;
    public const int EvtWindowCreated                  = 108;
    public const int EvtWindowOpenFailed               = 109;
    public const int EvtTrayToggleChanged              = 110;
    public const int EvtThemeApplied                   = 111;
    public const int EvtThemeUnknown                   = 112;
    public const int EvtAmbientStartFailed             = 113;
    public const int EvtAnytypeRuntimeEnded            = 114;
    public const int EvtAnytypeHostDrainIncomplete     = 115;
    // 116 retired.
    public const int EvtHotkeyPressIgnored             = 117;
    public const int EvtModuleStarting                 = 118;
    public const int EvtModuleRefused                  = 119;
    public const int EvtLaunchContextDetail            = 120;
    public const int EvtPreviousRunEndedUncleanly      = 121;
    public const int EvtLegacySettingsMigrationFailed  = 122;
}
