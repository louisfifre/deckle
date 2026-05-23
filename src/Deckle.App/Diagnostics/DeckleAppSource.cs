using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// Host App provider. Couvre App.xaml.cs (cycle de vie process, status,
// hotkey, observers ambient, shutdown/restart, command-line, post-build,
// HUD wiring) ainsi que les surfaces HUD (HudWindow / HudOverlayManager)
// et la LogWindow elle-même. Tout ce qui ne tient pas dans un module
// spécifique mais vit dans l'app hôte passe par ce provider.
//
// Provider Name = "Deckle" (sans suffixe) → tag [APP] via le bridge
// LegacyLogWindowSink (qui a une règle spéciale pour ce nom). Le legacy
// utilisait à la fois [APP] et [STATUS] pour différentes émissions du
// host — [STATUS] est conservé comme nom d'event mais le tag bridge
// reste [APP].
[EventSource(Name = "Deckle")]
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

    // ── Crash safety net ────────────────────────────────────────────────

    [Event(EvtCrashUnhandled,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}: {1}")]
    public void CrashUnhandled(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashUnhandled, ex_type, ex_message);
    }

    [Event(EvtCrashAppDomain,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "[AppDomain] {0}: {1}")]
    public void CrashAppDomain(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomain, ex_type, ex_message);
    }

    [Event(EvtCrashTaskScheduler,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "[TaskScheduler] {0}: {1}")]
    public void CrashTaskScheduler(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashTaskScheduler, ex_type, ex_message);
    }

    [Event(EvtCrashStackTrace,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
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

    // ── Status (route vers tag [STATUS] côté legacy) ────────────────────
    //
    // Le legacy utilisait LogSource.Status comme tag distinct. Côté
    // EventSource, le tag dérive du provider — donc ce sera [APP] dans
    // le LogWindow. C'est une régression visuelle mineure mais cohérente
    // avec la doctrine un-provider-par-module. Si Louis veut récupérer
    // le tag [STATUS] séparément, on remettra une exception dans le
    // bridge LegacyLogWindowSink keyed sur le nom d'event.

    [Event(EvtStatusChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void StatusChanged(string status)
    {
        if (IsEnabled()) WriteEvent(EvtStatusChanged, status);
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
           Message = "{0}")]
    public void ShutdownWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownWarning, message);
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
           Message = "shell-execute relaunch failed: {0}")]
    public void PostBuildRelaunchFailed(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPostBuildRelaunchFailed, ex_message);
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
           Message = "Ambient pipeline state: {0}")]
    public void AmbientPipelineState(string state)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPipelineState, state);
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
           Message = "Ambient start observer failed — {0}: {1}")]
    public void AmbientStartFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientStartFailed, ex_type, ex_message);
    }

    // ── Hotkey (App-side observer) ──────────────────────────────────────

    [Event(EvtHotkeyStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Start ({0})")]
    public void HotkeyStart(string hotkey_label)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStart, hotkey_label);
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
           Message = "{0} pressed — no profile bound, ignoring")]
    public void HotkeyNoProfile(string hotkey_name)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyNoProfile, hotkey_name);
    }

    // ── HUD / LogWindow surfaces (host-owned) ───────────────────────────

    [Event(EvtHudWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void HudWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHudWarning, message);
    }

    [Event(EvtLogWindowWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void LogWindowWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtLogWindowWarning, message);
    }

    // ── UserFeedback (HUD bridge) ───────────────────────────────────────
    // Canal canonique pour les notifications utilisateur émises depuis
    // l'app hôte. Severity 0/1/2 = Info/Warning/Error, role 0/1 =
    // Replacement/Overlay. Filtré par HudFeedbackEventListener.

    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }
}
