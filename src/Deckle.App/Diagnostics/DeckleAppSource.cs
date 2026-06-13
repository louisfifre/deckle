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
public sealed class DeckleAppSource : DeckleEventSource
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

    // ── Crash safety net ────────────────────────────────────────────────

    [Event(EvtCrashUnhandled,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Unhandled exception caught")]
    public void CrashUnhandled()
    {
        if (IsEnabled()) WriteEvent(EvtCrashUnhandled);
    }

    [Event(EvtCrashUnhandledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "unhandled exception | error={0} | message={1}")]
    public void CrashUnhandledDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashUnhandledDetail, ex_type, ex_message);
    }

    [Event(EvtCrashAppDomain,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Unhandled exception caught on the AppDomain")]
    public void CrashAppDomain()
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomain);
    }

    [Event(EvtCrashAppDomainDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "appdomain unhandled exception | error={0} | message={1}")]
    public void CrashAppDomainDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomainDetail, ex_type, ex_message);
    }

    [Event(EvtCrashTaskScheduler,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An unobserved task exception was caught")]
    public void CrashTaskScheduler()
    {
        if (IsEnabled()) WriteEvent(EvtCrashTaskScheduler);
    }

    [Event(EvtCrashTaskSchedulerDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "task scheduler unobserved exception | error={0} | message={1}")]
    public void CrashTaskSchedulerDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashTaskSchedulerDetail, ex_type, ex_message);
    }

    // Demoted to Verbose: a bare stack trace carries no user-facing value on
    // its own — it is the technical companion of the crash milestones above.
    // Kept at its frozen id (no rename, no mirror).
    [Event(EvtCrashStackTrace,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "crash stack trace | stack={0}")]
    public void CrashStackTrace(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtCrashStackTrace, stack_trace);
    }

    [Event(EvtProcessExit,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ProcessExit triggered")]
    public void ProcessExit()
    {
        if (IsEnabled()) WriteEvent(EvtProcessExit);
    }

    // ── Boot ────────────────────────────────────────────────────────────

    [Event(EvtPathsInitialized,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Paths initialized")]
    public void PathsInitialized()
    {
        if (IsEnabled()) WriteEvent(EvtPathsInitialized);
    }

    [Event(EvtPathsDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "paths | root={0} | settings={1} | telemetry={2} | models={3} | native={4}")]
    public void PathsDetail(string root, string settings, string telemetry, string models, string native)
    {
        if (IsEnabled()) WriteEvent(EvtPathsDetail, root, settings, telemetry, models, native);
    }

    [Event(EvtStartupMilestones,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "startup milestones | {0}")]
    public void StartupMilestones(string milestones_text)
    {
        if (IsEnabled()) WriteEvent(EvtStartupMilestones, milestones_text);
    }

    // ── Status ──────────────────────────────────────────────────────────
    //
    // StatusChanged stays on the App provider: LogWindow displays it under
    // [APP], consistent with the one-provider-per-module doctrine.

    [Event(EvtStatusChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Status changed")]
    public void StatusChanged()
    {
        if (IsEnabled()) WriteEvent(EvtStatusChanged);
    }

    [Event(EvtStatusChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "status changed | status={0}")]
    public void StatusChangedDetail(string status)
    {
        if (IsEnabled()) WriteEvent(EvtStatusChangedDetail, status);
    }

    // ── Shutdown / Restart ──────────────────────────────────────────────

    [Event(EvtShutdownRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Shutdown requested")]
    public void ShutdownRequested()
    {
        if (IsEnabled()) WriteEvent(EvtShutdownRequested);
    }

    [Event(EvtShutdownWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A shutdown step failed")]
    public void ShutdownWarning()
    {
        if (IsEnabled()) WriteEvent(EvtShutdownWarning);
    }

    [Event(EvtShutdownWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shutdown step failed | message={0}")]
    public void ShutdownWarningDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownWarningDetail, message);
    }

    [Event(EvtRestartRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restart requested")]
    public void RestartRequested()
    {
        if (IsEnabled()) WriteEvent(EvtRestartRequested);
    }

    [Event(EvtRestartFromTrayRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restart from tray requested")]
    public void RestartFromTrayRequested()
    {
        if (IsEnabled()) WriteEvent(EvtRestartFromTrayRequested);
    }

    [Event(EvtRestartSpawnNewProcess,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "spawn new process | exe={0} | args={1}")]
    public void RestartSpawnNewProcess(string exe_path, string args)
    {
        if (IsEnabled()) WriteEvent(EvtRestartSpawnNewProcess, exe_path, args);
    }

    [Event(EvtPostBuildRestartRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Post-build self-restart requested")]
    public void PostBuildRestartRequested()
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRestartRequested);
    }

    [Event(EvtPostBuildShellExecute,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shell-execute relaunch | exe={0}")]
    public void PostBuildShellExecute(string exe_path)
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildShellExecute, exe_path);
    }

    [Event(EvtPostBuildRelaunchFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not relaunch after build")]
    public void PostBuildRelaunchFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRelaunchFailed);
    }

    [Event(EvtPostBuildRelaunchFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shell-execute relaunch failed | message={0}")]
    public void PostBuildRelaunchFailedDetail(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRelaunchFailedDetail, ex_message);
    }

    // ── Command-line ────────────────────────────────────────────────────

    [Event(EvtCmdLineSettingsFlag,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "--settings flag detected | page={0}")]
    public void CmdLineSettingsFlag(string page_tag)
    {
        if (IsEnabled()) WriteEvent(EvtCmdLineSettingsFlag, page_tag);
    }

    [Event(EvtCmdLinePostBuildFlag,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "--post-build flag detected | scheduling shell-execute relaunch in 800ms")]
    public void CmdLinePostBuildFlag()
    {
        if (IsEnabled()) WriteEvent(EvtCmdLinePostBuildFlag);
    }

    // ── Ambient observer ────────────────────────────────────────────────

    [Event(EvtAmbientPipelineState,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient pipeline state changed")]
    public void AmbientPipelineState()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPipelineState);
    }

    [Event(EvtAmbientPipelineStateDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ambient pipeline state | state={0}")]
    public void AmbientPipelineStateDetail(string state)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPipelineStateDetail, state);
    }

    [Event(EvtAmbientMasterForcedOff,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient master toggle forced OFF at boot — explicit user action required to enable")]
    public void AmbientMasterForcedOff()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientMasterForcedOff);
    }

    [Event(EvtAmbientStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient lighting could not start")]
    public void AmbientStartFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientStartFailed);
    }

    [Event(EvtAmbientStartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ambient start failed | error={0} | message={1}")]
    public void AmbientStartFailedDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientStartFailedDetail, ex_type, ex_message);
    }

    // ── Hotkey (App-side observer) ──────────────────────────────────────

    [Event(EvtHotkeyStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Recording started")]
    public void HotkeyStart()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStart);
    }

    [Event(EvtHotkeyStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "recording started | hotkey={0}")]
    public void HotkeyStartDetail(string hotkey_label)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStartDetail, hotkey_label);
    }

    [Event(EvtHotkeyStop,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Stop")]
    public void HotkeyStop()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStop);
    }

    [Event(EvtHotkeyNoProfile,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A rewrite hotkey was pressed with no profile bound")]
    public void HotkeyNoProfile()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyNoProfile);
    }

    [Event(EvtHotkeyNoProfileDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "hotkey ignored, no profile bound | hotkey={0}")]
    public void HotkeyNoProfileDetail(string hotkey_name)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyNoProfileDetail, hotkey_name);
    }

    // ── HUD / LogWindow surfaces (host-owned) ───────────────────────────

    [Event(EvtHudWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The HUD reported a warning")]
    public void HudWarning()
    {
        if (IsEnabled()) WriteEvent(EvtHudWarning);
    }

    [Event(EvtHudWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hud warning | message={0}")]
    public void HudWarningDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHudWarningDetail, message);
    }

    [Event(EvtLogWindowWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The log window reported a warning")]
    public void LogWindowWarning()
    {
        if (IsEnabled()) WriteEvent(EvtLogWindowWarning);
    }

    [Event(EvtLogWindowWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "log window warning | message={0}")]
    public void LogWindowWarningDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtLogWindowWarningDetail, message);
    }

    // ── UserFeedback (HUD bridge) ───────────────────────────────────────
    // Canonical channel for user notifications emitted from the host app.
    // Severity 0/1/2 = Info/Warning/Error, role 0/1 = Replacement/Overlay.
    // Filtered by HudFeedbackEventListener.

    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }
}
