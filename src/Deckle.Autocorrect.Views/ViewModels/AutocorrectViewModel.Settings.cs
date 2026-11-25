using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Autocorrect.Views;

// ── AutocorrectViewModel — settings manifest ──────────────────────────────────
//
// The declarative half of AutocorrectPage's composed settings, kept beside the
// ViewModel that owns the values rather than in the page code-behind.
// SettingsComposer builds each SettingDescriptor into its host panel.
//
// The master toggle's default is the first-run value for this machine
// (AutocorrectSettings.CreateDefault: on only when Windows is in French), so the card gets a per-card reset that goes active exactly when
// the value leaves that default.
public partial class AutocorrectViewModel
{
    public IReadOnlyList<SettingDescriptor> AutocorrectSettingsManifest =>
    [
        Setting.Toggle("AutocorrectPage_MasterCard",
            () => Enabled,
            value => Enabled = value,
            defaultValue: () => AutocorrectSettings.CreateDefault(SystemLanguages.Current).Enabled),
    ];

    // The Paragraph correction section's own card, composed in its own host. Its
    // default is the settings' own (on): nothing is offered until Ollama and its
    // model are there.
    public IReadOnlyList<SettingDescriptor> ParagraphCorrectionManifest =>
    [
        Setting.Toggle("AutocorrectPage_ParagraphCard",
            () => ParagraphCorrection,
            value => ParagraphCorrection = value,
            defaultValue: () => new AutocorrectSettings().ParagraphCorrection),
    ];
}
