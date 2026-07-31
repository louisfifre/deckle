using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── LlmGeneralViewModel — settings manifest ───────────────────────────────────
//
// The declarative half of the Rewriting page's General section, kept beside the
// ViewModel that owns the values. Only the two cleanly-composable leaves of the
// whole page compose here:
//
//   • Enabled — the master toggle. When off, the page MASKS (collapses) every
//     dependent section (endpoint, shortcut slots, rules, profiles, models),
//     driven from LlmGeneralSection off this VM's Enabled PropertyChanged — the
//     composed toggle writes the same property, so the gating still fires.
//   • OllamaEndpoint — the local Ollama URL, a free-form string (Setting.Text,
//     single-line — a URL, not a folder path). VisibleWhen(() => Enabled) so it
//     collapses with the rest of the page when rewriting is off (mask-not-grey).
//
// Everything else on the page stays BESPOKE: the profiles, the auto-rules, the
// Ollama-model list, and the shortcut slots are dynamic runtime collections with
// add/remove/edit gestures no composer kind models. They keep their own sections.
//
// Each descriptor's default reads the POCO initializer (new LlmSettings().<Field>)
// — the same literal LlmSettingsService persists — so each card gets a per-card
// reset that goes active exactly when the value leaves that default. The two
// cards reuse the SAME x:Uid the hand-authored controls carried (LlmEnableCard,
// LlmEndpointExpander), so the composer resolves the identical Header and
// Description from this module's .resw.
public partial class LlmGeneralViewModel
{
    public IReadOnlyList<SettingDescriptor> GeneralSettingsManifest =>
    [
        Setting.Toggle("LlmEnableCard",
            () => Enabled,
            value => Enabled = value,
            glyph: Glyphs.Lightning,
            defaultValue: () => new LlmSettings().Enabled),

        Setting.Text("LlmEndpointExpander",
            () => OllamaEndpoint,
            value => OllamaEndpoint = value,
            new TextArgs(Placeholder: "http://localhost:11434/api/generate"),
            glyph: Glyphs.Endpoint,
            // Mask-not-grey: the endpoint hides with the rest of the page when
            // rewriting is off (Louis's decision), rather than greying.
            visibleWhen: () => Enabled,
            defaultValue: () => new LlmSettings().OllamaEndpoint),
    ];
}
