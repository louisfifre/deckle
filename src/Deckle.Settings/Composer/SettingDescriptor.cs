using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Deckle.Settings;

// ── SettingDescriptor ────────────────────────────────────────────────────────
//
// A single setting, declared once near the code that owns its value and
// consumed by the SettingsComposer to build a SettingsCard. It is deliberately
// UI-agnostic: it names a UI Kind and carries typed value selectors, but holds
// no WinUI control — the composer owns control creation. This keeps the
// declaration constructible from a ViewModel (which must not depend on XAML)
// while the composer, on the UI side, turns it into a card.
//
// Construct through the Setting.* factories rather than this initializer: they
// enforce the value type per Kind (a Toggle's selectors are Func<bool>, etc.)
// and box into the object-typed GetValue/SetValue the composer dispatches on.
public sealed record SettingDescriptor
{
    // The control family the composer renders for this setting.
    public required SettingKind Kind { get; init; }

    // Localization key shared by the .resw entries "<LabelKey>.Header" and
    // (optional) "<LabelKey>.Description" — the same key an x:Uid would carry.
    public required string LabelKey { get; init; }

    // Header glyph character, taken from the Glyphs.* constants (e.g.
    // Glyphs.Lightbulb) — the C# mirror of Icons.xaml kept for programmatic
    // FontIcons. Null for no icon.
    public string? Glyph { get; init; }

    // When true, the setting is shown only when the surface is in its advanced
    // "dose". Applied at compose time.
    public bool IsAdvanced { get; init; }

    // Per-component arguments (the "+ arguments" of "component + arguments +
    // visibility"), typed via a SettingArgs subtype — e.g. a folder picker's
    // "configure vs open-only". Null when the component needs none, as a toggle
    // does. The composer reads it inside the Kind's case.
    public SettingArgs? Args { get; init; }

    // Typed value access, boxed. The factories build these from Func<T>/Action<T>
    // so the call sites stay strongly typed and reflection-free.
    public required Func<object?> GetValue { get; init; }
    public required Action<object?> SetValue { get; init; }

    // The resettable default, boxed and lazy exactly like GetValue — the factories
    // build it from a Func<T> so the call site (e.g. () => new AppearanceSettings()
    // .Theme) reads the POCO initializer, the single source of truth for defaults.
    // Lazy because a default may not exist at declaration time (it is re-read on
    // each reset, never cached). Null means the setting has no resettable default —
    // a runtime-enumerated value such as a device id, where "default" is meaningless
    // — and the composer then renders no reset affordance for it.
    public Func<object?>? Default { get; init; }

    // Optional reactive state, re-evaluated whenever the source model raises
    // PropertyChanged. EnabledWhen greys the row out; VisibleWhen collapses it
    // entirely. The call site picks which fits the setting (per the settings-UX
    // doctrine, a setting that makes no sense in a context is hidden, not
    // disabled — but a dependent that is merely unavailable is greyed).
    public Func<bool>? EnabledWhen { get; init; }
    public Func<bool>? VisibleWhen { get; init; }

    // Optional gate run ONLY on the OFF→ON transition, before the enable is
    // allowed to persist — its Task<bool> resolving true lets the value flip on,
    // false leaves it off and writes nothing. Turning the setting back off never
    // runs it (disabling is always free). The XamlRoot is passed so the gate can
    // host a ContentDialog; it is read at user-interaction time, when the host is
    // live. Null for the vast majority of toggles — only a leaf that must secure
    // explicit consent before activating (the Diagnostics privacy opt-ins) carries
    // one, and the composer holds the write back until it returns.
    public Func<XamlRoot, Task<bool>>? ConfirmOnEnable { get; init; }
}
