using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Diagnostics.Logging;

// ── DiagnosticsViewModel — settings manifest ──────────────────────────────────
//
// The declarative half of the page, kept beside the ViewModel that owns the
// values rather than in the page code-behind. Each entry declares one setting —
// its kind, its localization key, its glyph, and typed selectors onto this VM's
// own properties — and SettingsComposer turns the list into SettingsCards.
//
// This is the "declare near the code, the surface composes itself" shape: the
// declaration lives with the values (same class, separate file — the per-module
// manifest), and the page only hosts. Adding or moving a setting is editing this
// list, never hand-authoring a card in XAML.
public partial class DiagnosticsViewModel
{
    // Runtime emission filters (the "Logging" section). Plain toggles today; the
    // manifest grows with the section. Selectors point straight at this VM's
    // properties — the change handlers (OnXChanged → PushLoggingToSettings) and
    // persistence are unchanged, the composer only drives the UI.
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
        Setting.Toggle("LoggingWindowingCard",
            () => LogWindowingActivity,
            value => LogWindowingActivity = value,
            glyph: Glyphs.Window),
    ];

    // Telemetry opt-ins (the "Telemetry" section). One composable toggle now:
    // Application log carries a confirmOnEnable gate so the composer holds its
    // OFF→ON write behind the consent dialog. Now that the page lives in its own
    // module, that dialog is reached through the Catalog registry
    // (TelemetryConsent.RequestApplicationLog, a method group the App wires at boot)
    // rather than the shell's dialog class directly — so the module gates its enable
    // behind consent without referencing Deckle.Settings. Dataset-specific opt-ins
    // live on the pages of the modules they observe. Application log is this page's
    // one composable telemetry toggle.
    //
    // No defaultValue on the consent toggle: a privacy opt-in has no "resettable
    // default" affordance per row (the section "Reset" clears it), so the composer
    // renders no per-card reset wheel for it — which is correct.
    public IReadOnlyList<SettingDescriptor> ApplicationLogSettings =>
    [
        Setting.Toggle("GeneralAppLogCard",
            () => ApplicationLogToDisk,
            value => ApplicationLogToDisk = value,
            glyph: Glyphs.AppLog,
            confirmOnEnable: TelemetryConsent.RequestApplicationLog),
    ];

    // Storage folder — the shared JSONL root for every telemetry stream. A Path
    // descriptor (FolderPickerMode.Configure: read-only readout with Change + Open)
    // over the TelemetryStorageDirectory string the VM owns; its
    // OnTelemetryStorageDirectoryChanged (PushTelemetryToSettings) rides the setter
    // unchanged, exactly as the General backup-location Path does. DefaultPath is
    // the deferred AppPaths lookup the code-behind used to push into the picker (the
    // empty-value fallback = <UserDataRoot>\telemetry\), moved here into PathArgs so
    // the manifest carries it. The reset default is the POCO initializer (empty →
    // "empty means AppPaths"), the one source of truth.
    //
    // Its own manifest composes into a dedicated host because the path is a storage
    // configuration row, not a consent toggle. The Path card is otherwise wired like
    // any other composed row.
    public IReadOnlyList<SettingDescriptor> StorageFolderSettings =>
    [
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
