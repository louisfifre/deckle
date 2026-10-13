using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Diagnostics.Logging;

// The page's settings manifest, kept beside the ViewModel that owns the
// values. SettingsComposer turns each list into SettingsCards; adding or
// moving a setting edits this list, never a card in XAML.
public partial class DiagnosticsViewModel
{
    public IReadOnlyList<SettingDescriptor> LoggingSettings =>
    [
        Setting.Toggle("LoggingAmbientCard",
            () => LogAmbientCaptureActivity,
            value => LogAmbientCaptureActivity = value,
            glyph: Glyphs.Lightbulb),
        Setting.Toggle("LoggingTranscriptionCard",
            () => LogTranscriptionActivity,
            value => LogTranscriptionActivity = value,
            glyph: Glyphs.Speech),
        Setting.Toggle("LoggingAutocorrectCard",
            () => LogAutocorrectActivity,
            value => LogAutocorrectActivity = value,
            glyph: Glyphs.Language),
        Setting.Toggle("LoggingInputCard",
            () => LogInputActivity,
            value => LogInputActivity = value,
            glyph: Glyphs.Mouse,
            defaultValue: () => new LoggingSettings().LogInputActivity),
        Setting.Toggle("LoggingWindowingCard",
            () => LogWindowingActivity,
            value => LogWindowingActivity = value,
            glyph: Glyphs.Window),
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
            glyph: Glyphs.AppLog,
            confirmOnEnable: TelemetryConsent.RequestApplicationLog),
    ];

    // The mouse-wheel dataset opt-in and the shared JSONL root for every
    // telemetry stream, in their own host: dataset capture and its storage
    // location belong together, apart from the application-log consent. The
    // path's reset default is the POCO initializer (empty means
    // AppPaths.TelemetryDirectory).
    public IReadOnlyList<SettingDescriptor> TelemetrySettings =>
    [
        Setting.Toggle("TelemetryMouseWheelCard",
            () => RecordWheelEvents,
            value => RecordWheelEvents = value,
            glyph: Glyphs.Mouse,
            defaultValue: () => new Deckle.Input.MouseWheelSettings().RecordEvents),
        Setting.Path("GeneralStorageFolderCard",
            () => TelemetryStorageDirectory,
            value => TelemetryStorageDirectory = value,
            new PathArgs(
                FolderPickerMode.Configure,
                DefaultPath: () => AppPaths.TelemetryDirectory),
            glyph: Glyphs.Folder,
            defaultValue: () => new TelemetrySettings().StorageDirectory),
    ];
}
