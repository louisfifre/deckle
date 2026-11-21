using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hosting;

namespace Deckle.App;

// The published Deckle's part of the boot-time diagnostics wiring: the sinks
// only this host has (the application trace under the developer-mode
// retention, the Log window's buffer, the HUD feedback line) and telemetry.
// The one EventListener, the local setup and log files, the boot release and
// the flush barrier are the shared kit's (DiagnosticsHost): every app of the
// suite boots the same way.
//
// Phase order is load-bearing: InitializeLocalSinks runs before the settings
// load, the operational sinks after it, and telemetry registers only consented
// datasets under telemetry/. The listener keeps the boot history until
// CompleteBoot, so a sink of a later phase still starts at AppStarting.
internal static class AppDiagnosticsBootstrap
{
    private static LogWindowSink? _logWindowSink;
    private static bool _hudAttached;

    // The crash detail and stack events are Verbose, so a level predicate alone
    // would split a crash across files; the log allowlists them by name.
    // The bare name holds while no other Deckle-* provider declares these names;
    // scope the check by provider if one ever does.
    private static readonly HashSet<string> _crashCompanions = new()
    {
        nameof(DeckleAppSource.CrashAppDomainDetail),
        nameof(DeckleAppSource.CrashStackTrace),
    };

    public static void InitializeLocalSinks(string diagnosticsDirectory)
        => DiagnosticsHost.InitializeLocalSinks(diagnosticsDirectory, _crashCompanions);

    public static void InitializeOperationalSinks(string diagnosticsDirectory)
    {
        if (_logWindowSink is not null) return;

        var dispatch = DiagnosticsHost.Dispatch;
        // The trace keeps more history in developer mode (TraceRetentionPolicy),
        // read at each roll so the mode applies without a restart.
        dispatch.AddSink(
            new ApplicationLogSink(
                diagnosticsDirectory,
                maxArchives: static () => TraceRetentionPolicy.MaxArchives(
                    Settings.SettingsService.Instance.Current.Developer.DeveloperMode)),
            replayBoot: true);

        // Buffers from boot: the lazy LogWindow attaches on first open and
        // receives the buffer as a replay.
        _logWindowSink = new LogWindowSink();
        dispatch.AddSink(_logWindowSink, replayBoot: true);
    }

    public static void InitializeTelemetry(string telemetryDirectory)
    {
        TelemetryListenerBootstrap.Configure(
            DiagnosticsHost.Dispatch,
            telemetryDirectory,
            validationSubdirectory: false);
    }

    // After the last phase-registered sink: the boot history is released and
    // any sink added later (the HUD feedback sink) starts from its attach.
    public static void CompleteBoot() => DiagnosticsHost.CompleteBoot();

    // UserFeedbackEmitted before this call is dropped: the sink needs the live
    // HUD surfaces, so it registers once they exist.
    public static void AttachHudFeedbackSink(IHudFeedbackSink sink)
    {
        if (_hudAttached) return;
        _hudAttached = true;
        DiagnosticsHost.Dispatch.AddSink(new HudFeedbackSink(sink));
    }

    public static void AttachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowSink?.AttachSink(sink);
    }

    public static void DetachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowSink?.DetachSink(sink);
    }

    public static void ClearLogWindowHistory()
    {
        _logWindowSink?.ClearBuffer();
    }

    // Every background JSONL writer drains the entries accepted before this
    // call, or gives up at the deadline. True when every destination drained.
    public static bool Flush(TimeSpan timeout) => DiagnosticsHost.Flush(timeout);
}