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
//
// The module's two dataset consents live in AutocorrectTelemetryViewModel,
// composed on the Diagnostics page.
public partial class AutocorrectViewModel
{
    public IReadOnlyList<SettingDescriptor> AutocorrectSettingsManifest =>
    [
        Setting.Toggle("AutocorrectPage_MasterCard",
            () => Enabled,
            value => Enabled = value,
            defaultValue: () => new AutocorrectSettings().Enabled),
    ];
}
