using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── LlmGeneralViewModel — settings manifest ───────────────────────────────────
//
// The declarative half of the Rewriting page's General section. Only these two
// leaves compose: the active profile, the profiles and the Ollama-model list are
// dynamic runtime collections with add/remove/edit gestures no composer kind
// models, so they keep their own hand-authored sections.
//
// Each descriptor's default reads the POCO initializer (new LlmSettings().<Field>),
// so each card's inline reset goes active exactly when the value leaves that
// default. The keys (LlmEnableCard, LlmEndpointExpander) resolve Header and
// Description from this module's .resw and are the Tags SettingsSearch targets.
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
            // rewriting is off, rather than greying.
            visibleWhen: () => Enabled,
            defaultValue: () => new LlmSettings().OllamaEndpoint),
    ];
}
