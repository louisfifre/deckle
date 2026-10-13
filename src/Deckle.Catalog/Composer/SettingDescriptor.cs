using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── SettingDescriptor ────────────────────────────────────────────────────────
//
// A single setting, declared near the code that owns its value and turned into
// a card by SettingsComposer. It holds no WinUI control, so a ViewModel, which
// must not depend on XAML, can build it.
//
// Construct through the Setting.* factories rather than this initializer: they
// enforce the value type per Kind and box into the object-typed
// GetValue/SetValue the composer dispatches on.
public sealed record SettingDescriptor
{
    public required SettingKind Kind { get; init; }

    // Localization key shared by the .resw entries "<LabelKey>.Header" and
    // (optional) "<LabelKey>.Description" — the same key an x:Uid would carry.
    public required string LabelKey { get; init; }

    // A Glyphs.* character; null for no icon.
    public string? Glyph { get; init; }

    // Shown only when the surface displays its advanced settings; applied at
    // compose time.
    public bool IsAdvanced { get; init; }

    public SettingArgs? Args { get; init; }

    public required Func<object?> GetValue { get; init; }
    public required Action<object?> SetValue { get; init; }

    // Lazy and re-read on each reset, never cached. Point it at the POCO
    // initializer, the single source of truth for defaults. Null when "default" is
    // meaningless (a runtime-enumerated value such as a device id); the composer
    // then renders no reset.
    public Func<object?>? Default { get; init; }

    // Re-evaluated whenever the source model raises PropertyChanged. EnabledWhen
    // greys the row out; VisibleWhen collapses it. Per the settings-UX doctrine, a
    // setting that makes no sense in a context is hidden, not disabled; a
    // dependent that is merely unavailable is greyed.
    public Func<bool>? EnabledWhen { get; init; }
    public Func<bool>? VisibleWhen { get; init; }

    // The message to show under the card, or null; re-evaluated on every refresh.
    // One channel, no warning/error split: the wording carries the tone.
    public Func<string?>? Advisory { get; init; }

    // Runs only on the OFF→ON transition: true lets the value flip on, false
    // leaves it off and writes nothing. Turning the setting off never runs it. The
    // XamlRoot lets the gate host a ContentDialog.
    public Func<XamlRoot, Task<bool>>? ConfirmOnEnable { get; init; }
}
