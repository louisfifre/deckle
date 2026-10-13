using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── LlmGeneralViewModel — settings manifests ──────────────────────────────────
//
// The declarative half of the Rewriting page's General block. Only two leaves
// compose, in two lists because they sit in two places: the module switch comes
// first on the page, alone, outside any section; the endpoint sits in the
// General section. The active profile, the profiles and the Ollama-model list
// are dynamic runtime collections no composer kind models, so they keep their
// own hand-authored sections.
//
// Each descriptor's default reads the POCO initializer (new LlmSettings().<Field>),
// so each card's inline reset goes active exactly when the value leaves that
// default. The keys (LlmEnableCard, LlmEndpointExpander) are the Tags
// SettingsSearch targets.
public partial class LlmGeneralViewModel
{
    public IReadOnlyList<SettingDescriptor> ModuleSwitch =>
    [
        Setting.Toggle("LlmEnableCard",
            () => Enabled,
            value => Enabled = value,
            glyph: Glyphs.Lightning,
            defaultValue: () => new LlmSettings().Enabled),
    ];

    public IReadOnlyList<SettingDescriptor> GeneralSettingsManifest =>
    [
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
