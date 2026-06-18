using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── GeneralViewModel — settings manifest ──────────────────────────────────────
//
// The declarative half of the page, kept beside the ViewModel that owns the
// values rather than in the page code-behind. Each entry declares one setting —
// its kind, its localization key, its glyph, and typed selectors onto this VM's
// own properties — and SettingsComposer turns the list into SettingsCards.
//
// Only the section's flat, value-backed cards live here. GeneralPage's other
// controls stay hand-authored: the read-only shortcut readouts, the overlay
// SettingsExpander and its position ComboBox (structural grouping the composer
// has no shape for, and a choice gated inside it), the autostart toggle (registry
// side-effect with revert-on-refusal), and the command/diagnostic cards under
// Application data. The composer drives only what it cleanly can.
public partial class GeneralViewModel
{
    // Appearance section — the theme picker. A Choice over the theme string the VM
    // already owns ("System"/"Light"/"Dark"), the values the ComboBoxItem Tags
    // carried; the option labels resolve from the page's .resw "<key>.Content"
    // entries. The change handler (OnThemeChanged → PushToSettings + ApplyTheme)
    // is unchanged: the composer drives the setter, the side effect rides along —
    // which is exactly why a card whose effect lives in the VM setter composes.
    public IReadOnlyList<SettingDescriptor> AppearanceSettings =>
    [
        Setting.Choice<string>("GeneralAppThemeCard",
            () => Theme,
            value => Theme = value,
            [
                ("System", "GeneralThemeSystem"),
                ("Light", "GeneralThemeLight"),
                ("Dark", "GeneralThemeDark"),
            ],
            glyph: Glyphs.Theme),
    ];

    // Behaviour section — the flat auto-paste toggle. The overlay group above it
    // stays a hand-authored SettingsExpander (its master/child toggles and the
    // position ComboBox are bound to the expander's structure), so this manifest
    // covers only the single composable card that sits below it. The change
    // handler (OnAutoPasteEnabledChanged → PushToSettings) and persistence are
    // unchanged; the composer only drives the UI.
    public IReadOnlyList<SettingDescriptor> BehaviourSettings =>
    [
        Setting.Toggle("GeneralAutoPasteCard",
            () => AutoPasteEnabled,
            value => AutoPasteEnabled = value,
            glyph: Glyphs.Paste),
    ];

    // Startup section — "start with Windows". A plain TwoWay toggle, but its
    // OnAutostartEnabledChanged writes HKCU and, when the write is refused
    // (GPO/ACL), reverts the property under the sync guard. That revert composes:
    // the composer drives the setter, and the reverted value rides back to the
    // toggle on PropertyChanged — the same path Load() and the section Reset use.
    // The textbook "side effect in the VM setter" card, migrated now that the
    // theme picker proved that path carries the effect through unchanged.
    public IReadOnlyList<SettingDescriptor> StartupSettings =>
    [
        Setting.Toggle("GeneralAutostartCard",
            () => AutostartEnabled,
            value => AutostartEnabled = value,
            glyph: Glyphs.Launch),
    ];
}
