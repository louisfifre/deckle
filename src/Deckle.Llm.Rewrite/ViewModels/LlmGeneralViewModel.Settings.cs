using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── LlmGeneralViewModel — settings manifest ───────────────────────────────────
//
// The declarative half of the Rewriting page's module switch: one leaf, first
// on the page, alone, outside any section. The active profile, the profiles,
// the providers and the Ollama-model list are dynamic runtime collections no
// composer kind models, so they keep their own hand-authored sections.
//
// The descriptor's default reads the POCO initializer (new LlmSettings().Enabled),
// so the card's inline reset goes active exactly when the value leaves that
// default. The key (LlmEnableCard) is the Tag SettingsSearch targets.
public partial class LlmGeneralViewModel
{
    public IReadOnlyList<SettingDescriptor> ModuleSwitch =>
    [
        Setting.Toggle("LlmEnableCard",
            () => Enabled,
            value => Enabled = value,
            defaultValue: () => new LlmSettings().Enabled),
    ];
}
