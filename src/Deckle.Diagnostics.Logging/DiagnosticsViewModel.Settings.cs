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
            value => LogAmbientCaptureActivity = value),
        Setting.Toggle("LoggingTranscriptionCard",
            () => LogTranscriptionActivity,
            value => LogTranscriptionActivity = value),
        Setting.Toggle("LoggingAutocorrectCard",
            () => LogAutocorrectActivity,
            value => LogAutocorrectActivity = value),
        Setting.Toggle("LoggingInputCard",
            () => LogInputActivity,
            value => LogInputActivity = value,
            defaultValue: () => new LoggingSettings().LogInputActivity),
        Setting.Toggle("LoggingWindowingCard",
            () => LogWindowingActivity,
            value => LogWindowingActivity = value),
    ];

    // The confirmOnEnable gate holds the OFF→ON write behind the consent
    // dialog, reached through the Catalog registry
    // (TelemetryConsent.RequestApplicationLog, wired by the App at boot) so this
    // module does not reference Deckle.Settings.
    //
    // No defaultValue: a privacy opt-in gets no per-card reset; the section
    // "Reset" clears it.
    public IReadOnlyList<SettingDescriptor> ApplicationLogSettings =>
    [
        Setting.Toggle("GeneralAppLogCard",
            () => ApplicationLogToDisk,
            value => ApplicationLogToDisk = value,
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

    // The telemetry storage folder, first card of the Folders section that
    // closes the page, above the diagnostics folder: each place Deckle writes
    // to sits in one section, after everything that writes there. The override
    // reaches only what reads CorpusPaths (today the dictation audio WAVs);
    // the JSONL sinks, trackpad and wheel recorders write under
    // AppPaths.TelemetryDirectory whatever it holds. The path's reset default
    // is the POCO initializer (empty means AppPaths.TelemetryDirectory).
    public IReadOnlyList<SettingDescriptor> TelemetryStorageSettings =>
    [
        Setting.Path("GeneralStorageFolderCard",
            () => TelemetryStorageDirectory,
            value => TelemetryStorageDirectory = value,
            new PathArgs(
                FolderPickerMode.Configure,
                DefaultPath: () => AppPaths.TelemetryDirectory),
            defaultValue: () => new TelemetrySettings().StorageDirectory),
    ];
}
