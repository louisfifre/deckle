using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── AutocorrectViewModel — settings manifest ──────────────────────────────────
//
// The declarative half of AutocorrectPage's persisted master switch, kept beside
// the ViewModel that owns the value rather than in the page code-behind. The one
// composable setting — the Enable-autocorrect master toggle — is declared here as
// a SettingDescriptor and SettingsComposer builds its SettingsCard into the host
// panel. It reuses the SAME x:Uid the hand-authored card carried
// (AutocorrectPage_MasterCard), so the composer resolves the identical Header and
// Description from this module's .resw.
//
// Only the master switch composes. The per-app Apps list stays BESPOKE
// (runtime-enumerated rows with add/remove/forget gestures — no composer kind
// models a live collection of cards), and it is gated on this switch by the page
// code-behind: when the master is off the whole Apps section is collapsed
// (mask-never-grey), reacting to the Enabled PropertyChanged the composer's setter
// raises.
//
// The default reads the POCO initializer (new AutocorrectSettings().Enabled — the
// same literal AutocorrectSettingsService persists), so the card gets a per-card
// reset that goes active exactly when the value leaves that default.
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

    // The module's own Diagnostics section — three flat, independent leaf
    // toggles, composed into the page's DiagnosticsHost. Relocated from the
    // shared Diagnostics page: the log-activity filter (no consent — nothing
    // leaves the device) and the two telemetry opt-ins, each gating its OFF→ON
    // flip behind a consent dialog via the Catalog registry method group the
    // composer runs. No defaultValue on any: like the other privacy opt-ins,
    // a per-row "resettable default" affordance is wrong here.
    //
    // Text is NOT gated by Decisions — on the origin page the nesting was
    // progressive disclosure, not a dependency — so they stay two independent
    // leaves, never a masking Group.
    public IReadOnlyList<SettingDescriptor> DiagnosticsSettings =>
    [
        Setting.Toggle("AutocorrectLogActivityCard",
            () => LogAutocorrectActivity,
            value => LogAutocorrectActivity = value,
            glyph: Glyphs.Language),
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
