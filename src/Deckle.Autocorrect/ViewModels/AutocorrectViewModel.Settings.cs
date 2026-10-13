using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── AutocorrectViewModel — settings manifest ──────────────────────────────────
//
// The declarative half of AutocorrectPage's composed settings, kept beside the
// ViewModel that owns the values rather than in the page code-behind.
// SettingsComposer builds each SettingDescriptor into its host panel.
//
// The master toggle's default reads the POCO initializer (new
// AutocorrectSettings().Enabled — the same literal AutocorrectSettingsService
// persists), so the card gets a per-card reset that goes active exactly when
// the value leaves that default.
public partial class AutocorrectViewModel
{
    public IReadOnlyList<SettingDescriptor> AutocorrectSettingsManifest =>
    [
        Setting.Toggle("AutocorrectPage_MasterCard",
            () => Enabled,
            value => Enabled = value,
            glyph: Glyphs.Autocorrect,
            defaultValue: () => new AutocorrectSettings().Enabled),
    ];

    // The module's own Diagnostics section: two telemetry opt-ins, each gating
    // its OFF→ON flip behind a consent dialog the composer runs. No defaultValue
    // on either: like the other privacy opt-ins, a per-row "resettable default"
    // affordance is wrong here. Text is NOT gated by Decisions, so they stay two
    // independent leaves, never a masking Group.
    public IReadOnlyList<SettingDescriptor> DiagnosticsSettings =>
    [
        Setting.Toggle("AutocorrectDecisionsCard",
            () => AutocorrectDecisions,
            value => AutocorrectDecisions = value,
            glyph: Glyphs.Language,
            confirmOnEnable: TelemetryConsent.RequestAutocorrectDecisions),
        Setting.Toggle("AutocorrectTextCard",
            () => AutocorrectText,
            value => AutocorrectText = value,
            glyph: Glyphs.Language,
            confirmOnEnable: TelemetryConsent.RequestAutocorrectText),
    ];
}
