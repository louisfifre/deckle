using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Diagnostics.Logging;

// The page's settings manifest, kept beside the ViewModel that owns the
// values. SettingsComposer turns each list into SettingsCards; adding or
// moving a setting edits this list, never a card in XAML. No glyph on any
// card: the rail carries the page's icon (2026-10-14).
//
// The page is the one editor of everything Deckle records about itself: the
// module-owned datasets (microphone level, dictation latency and corpus,
// autocorrect decisions and text, trackpad frames) compose into the Telemetry
// section beside TelemetrySettings, from DiagnosticsRegistry, with their own
// models. The page sorts that section, folds first, then flat cards.
public partial class DiagnosticsViewModel
{
    public IReadOnlyList<SettingDescriptor> LoggingSettings =>
    [
        Setting.Toggle("LoggingAmbientCard",
            () => LogAmbientCaptureActivity,
            value => LogAmbientCaptureActivity = value,
            defaultValue: () => new LoggingSettings().LogAmbientCaptureActivity),
        Setting.Toggle("LoggingTranscriptionCard",
            () => LogTranscriptionActivity,
            value => LogTranscriptionActivity = value,
            defaultValue: () => new LoggingSettings().LogTranscriptionActivity),
        Setting.Toggle("LoggingAutocorrectCard",
            () => LogAutocorrectActivity,
            value => LogAutocorrectActivity = value,
            defaultValue: () => new LoggingSettings().LogAutocorrectActivity),
        Setting.Toggle("LoggingInputCard",
            () => LogInputActivity,
            value => LogInputActivity = value,
            defaultValue: () => new LoggingSettings().LogInputActivity),
        Setting.Toggle("LoggingWindowingCard",
            () => LogWindowingActivity,
            value => LogWindowingActivity = value,
            defaultValue: () => new LoggingSettings().LogWindowingActivity),
    ];

    // The confirmOnEnable gate holds the OFF→ON write behind the consent
    // dialog, reached through the Catalog registry
    // (TelemetryConsent.RequestApplicationLog, wired by the App at boot) so this
    // module does not reference Deckle.Settings.
    //
    // A default exists, so the card has its reset (Louis, 2026-10-15: every
    // setting with a default gets its reset). The default is off, so the reset
    // only turns the opt-in off, a write the consent gate does not hold; the
    // composer's reset writes past the gate, so a default that turned an
    // opt-in on would skip the consent.
    public IReadOnlyList<SettingDescriptor> ApplicationLogSettings =>
    [
        Setting.Toggle("GeneralAppLogCard",
            () => ApplicationLogToDisk,
            value => ApplicationLogToDisk = value,
            defaultValue: () => new LoggingSettings().ApplicationLogToDisk,
            confirmOnEnable: TelemetryConsent.RequestApplicationLog),
    ];

    // The mouse-wheel dataset opt-in, the page's own source in the Telemetry
    // section. A flat card, so it leads the flat cards, under every module
    // fold.
    public IReadOnlyList<SettingDescriptor> TelemetrySettings =>
    [
        Setting.Toggle("TelemetryMouseWheelCard",
            () => RecordWheelEvents,
            value => RecordWheelEvents = value,
            defaultValue: () => new Deckle.Input.MouseWheelSettings().RecordEvents),
    ];

    // The Folders section that closes the page: each place Deckle writes to
    // sits in one section, after everything that writes there. Both are
    // folder folds (a standalone Path renders as one), their Location line
    // inside.
    //
    // The telemetry storage folder first, configurable. The override reaches
    // only what reads CorpusPaths (today the dictation audio WAVs); the JSONL
    // sinks, trackpad and wheel recorders write under
    // AppPaths.TelemetryDirectory whatever it holds. The path's reset default
    // is the POCO initializer (empty means AppPaths.TelemetryDirectory).
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
