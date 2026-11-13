using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;
using Deckle.Input;
using Deckle.Input.Trackpad;

namespace Deckle.Diagnostics.Logging.Views;

// The page's settings manifest, kept beside the ViewModel that owns the
// values. SettingsComposer turns each list into SettingsCards; adding or
// moving a setting edits this list, never a card in XAML. No glyph on any
// card: the rail carries the page's icon (2026-10-14).
//
// The page has three sections (2026-11-13). Logs: what Deckle writes about
// itself, normal by default, with one switch for the detail a developer
// turns on to chase a bug, and the copy kept on disk. Corpus: what the
// person says or types, kept on purpose, contributed by the modules that own
// it through DiagnosticsRegistry. Folders: where it all lands.
public partial class DiagnosticsViewModel
{
    // Detailed logs, one fold. Its switch is the one gesture for "give me
    // everything for this bug", meant for anyone, not only a developer: each
    // log source under it is on by default, so the switch alone gives every
    // source's detail, and the list underneath is there for whoever wants to
    // choose. Those sources write log lines, the kind a person shares.
    //
    // The developer captures come last and show only in developer mode
    // (Settings > General): they are datasets the analysis scripts read, not
    // lines to share, and they record only in developer mode too, so the list
    // an ordinary user sees is all the switch turns on. Two of them are
    // per-frame, warned of in their own words; the microphone level keeps the
    // consent it had as a dataset. Off, the switch stops every child whatever
    // its own toggle says (App.DeveloperCapturesAdmitted,
    // TelemetryListenerBootstrap.ReadDetailGate), and the children hide.
    public IReadOnlyList<SettingDescriptor> LogsSettings =>
    [
        Setting.Group("LoggingDetailedCard",
            () => DetailedLogs,
            value => DetailedLogs = value,
            defaultValue: () => new LoggingSettings().DetailedLogs,
            children:
            [
                Setting.Toggle("LoggingAmbientCard",
                    () => AmbientDetails, value => AmbientDetails = value,
                    defaultValue: () => new LoggingSettings().AmbientDetails),
                Setting.Toggle("LoggingTranscriptionCard",
                    () => TranscriptionDetails, value => TranscriptionDetails = value,
                    defaultValue: () => new LoggingSettings().TranscriptionDetails),
                Setting.Toggle("LoggingLlmCard",
                    () => LlmDetails, value => LlmDetails = value,
                    defaultValue: () => new LoggingSettings().LlmDetails),
                Setting.Toggle("LoggingAutocorrectCard",
                    () => AutocorrectDetails, value => AutocorrectDetails = value,
                    defaultValue: () => new LoggingSettings().AutocorrectDetails),
                Setting.Toggle("LoggingInputCard",
                    () => InputDetails, value => InputDetails = value,
                    defaultValue: () => new LoggingSettings().InputDetails),
                Setting.Toggle("LoggingWindowingCard",
                    () => WindowingDetails, value => WindowingDetails = value,
                    defaultValue: () => new LoggingSettings().WindowingDetails),

                Setting.Toggle("LoggingMicrophoneLevelCard",
                    () => MicrophoneLevel, value => MicrophoneLevel = value,
                    visibleWhen: () => DeveloperMode,
                    defaultValue: () => new TelemetrySettings().MicrophoneTelemetry,
                    confirmOnEnable: TelemetryConsent.RequestMicrophone),
                Setting.Toggle("LoggingTouchpadFramesCard",
                    () => RecordTouchpadFrames, value => RecordTouchpadFrames = value,
                    visibleWhen: () => DeveloperMode,
                    defaultValue: () => new TrackpadSettings().RecordFrames),
                Setting.Toggle("LoggingWheelEventsCard",
                    () => RecordWheelEvents, value => RecordWheelEvents = value,
                    visibleWhen: () => DeveloperMode,
                    defaultValue: () => new MouseWheelSettings().RecordEvents),
            ]),

        // The trace. How much history it keeps is decided, not set
        // (TraceRetentionPolicy: more in developer mode). The consent gate
        // holds the OFF→ON write behind its dialog
        // (TelemetryConsent.RequestApplicationLog).
        Setting.Toggle("GeneralAppLogCard",
            () => ApplicationLogToDisk,
            value => ApplicationLogToDisk = value,
            defaultValue: () => new LoggingSettings().ApplicationLogToDisk,
            confirmOnEnable: TelemetryConsent.RequestApplicationLog),
    ];

    // The Folders section that closes the page: each place Deckle writes to
    // sits in one section, after everything that writes there. Both are
    // folder folds (a standalone Path renders as one), their Location line
    // inside.
    //
    // The storage folder first, configurable. The override reaches only what
    // reads CorpusPaths (today the dictation audio WAVs); the JSONL sinks,
    // trackpad and wheel recorders write under AppPaths.TelemetryDirectory
    // whatever it holds, so the card is named for what it moves. The path's
    // reset default is the POCO initializer (empty means
    // AppPaths.TelemetryDirectory).
    //
    // Then the diagnostics folder, which the app owns and never moves: OpenOnly,
    // so the line shows the path and offers Open alone, and the setter is
    // never called. No defaultValue, so the section reset covers the storage
    // folder alone.
    public IReadOnlyList<SettingDescriptor> FoldersSettings =>
    [
        Setting.Path("GeneralStorageFolderCard",
            () => TelemetryStorageDirectory,
            value => TelemetryStorageDirectory = value,
            new PathArgs(
                FolderPickerMode.Configure,
                DefaultPath: () => AppPaths.TelemetryDirectory),
            defaultValue: () => new TelemetrySettings().StorageDirectory),
        Setting.Path("DiagnosticsFilesCard",
            () => AppPaths.DiagnosticsDirectory,
            _ => { },
            new PathArgs(
                FolderPickerMode.OpenOnly,
                DefaultPath: () => AppPaths.DiagnosticsDirectory)),
    ];
}
