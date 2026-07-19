using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // ── Per-card reset ─────────────────────────────────────────────────────────
    //
    // Wraps a value control with an inline reset affordance when its descriptor
    // carries a Default. Returns the element to assign as the card's Content and an
    // optional updateState closure the row's refresher must call: that closure
    // re-gates the button's IsEnabled off the live dirty-check (active-when-dirty,
    // the Playground model — a reset is offered only when the value actually drifts
    // from its default) and pins its tooltip.
    //
    // When the descriptor has no Default the value control is returned unchanged and
    // updateState is null — no reset chrome for a setting that has no resettable
    // default (a runtime-enumerated value), and the refresher then has nothing extra
    // to do.
    //
    // The button sits LEFT of the value control (least chrome, the value stays where
    // the eye expects it on the trailing edge), reveals on hover/focus exactly like
    // TunableRow, and registers this row's dirty-check and reset-action into the
    // composer's reset surface.
    private (FrameworkElement Content, Action? UpdateState) WrapWithReset(
        SettingsCard card, FrameworkElement valueControl, SettingDescriptor s)
    {
        if (s.Default is null) return (valueControl, null);

        Button reset = BuildResetButton();

        // Reveal on the CARD's hover and the BUTTON's keyboard focus — either keeps
        // it shown, so leaving one while the other holds doesn't hide it early.
        WireReveal(card, reset);

        // active-when-dirty: a difference from the default is what enables the
        // reset, evaluated live so it tracks every model change through the
        // refresher below.
        bool Dirty() => !DefaultEquals(s.GetValue(), s.Default!());
        _dirtyChecks.Add(Dirty);
        _resetActions.Add(() => s.SetValue(s.Default!()));

        reset.Click += (_, _) => s.SetValue(s.Default!());

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            // Tight spacing between the reset glyph and the value control — matches
            // the hand-authored per-card reset rows the composed cards sit beside.
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(reset);
        content.Children.Add(valueControl);

        // The tooltip string is constant, but resolved once here (not per refresh)
        // so the dirty-driven refresher only flips IsEnabled, never re-hits Loc.
        string tooltip = ResolveResetTooltip();
        void UpdateState()
        {
            reset.IsEnabled = Dirty();
            ToolTipService.SetToolTip(reset, tooltip);
        }

        return (content, UpdateState);
    }

    // A subtle 32×32 reset wheel, built imperatively to match the hand-authored
    // per-card reset (WhisperPage's ResetButtonStyle) and the Playground's
    // NewExpander: SubtleButtonStyle from the app root, a Glyphs.Refresh FontIcon,
    // and Opacity 0 at rest so it is invisible until the reveal shows it. The glyph
    // comes from the Glyphs.* C# mirror, the blessed programmatic-FontIcon path.
    private static Button BuildResetButton() => new()
    {
        Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
        Width = 32,
        Height = 32,
        Padding = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0,
        Content = new FontIcon { Glyph = Glyphs.Refresh, FontSize = 14 },
    };

    // Instant hover/focus reveal, mirroring TunableRow and the Playground's reset:
    // either the host being pointer-over OR the button holding keyboard focus shows
    // it; both released hides it. No Storyboard — a flat Opacity flip — and the
    // button keeps its layout slot at rest, so the reveal never reflows the row.
    private static void WireReveal(FrameworkElement host, Button button)
    {
        bool pointerOver = false, focused = false;
        void Update() => button.Opacity = pointerOver || focused ? 1 : 0;
        host.PointerEntered += (_, _) => { pointerOver = true; Update(); };
        host.PointerExited += (_, _) => { pointerOver = false; Update(); };
        button.GotFocus += (_, _) => { focused = true; Update(); };
        button.LostFocus += (_, _) => { focused = false; Update(); };
    }

    // The reset tooltip is composer-OWNED and identical for every module, so it
    // resolves from the host app's ROOT map — not the module .resw. (Headers are
    // module strings and resolve per-module; this one is not.) One source for the
    // single string every composed reset shares, whatever module the card lives in.
    private string ResolveResetTooltip()
        => Loc.Get("SettingsComposer_ResetToDefault");

    // Reactive enabled/visible: re-evaluated on every refresh. Null predicates
    // leave the framework defaults (enabled, visible) untouched.
    private static void ApplyReactiveState(Control control, SettingDescriptor s)
    {
        if (s.EnabledWhen is not null) control.IsEnabled = s.EnabledWhen();
        if (s.VisibleWhen is not null)
            control.Visibility = s.VisibleWhen() ? Visibility.Visible : Visibility.Collapsed;
    }

    // Resolves a card's header/description from the OWNING MODULE's .resw — the
    // same entries an x:Uid would read, but at runtime and from the module's own
    // PRI subtree, not the host app's root map. A code-created element gets no
    // x:Uid auto-resolution, and a module's strings are absent from the app root
    // map, so resolving via Loc.Get (root) would silently yield empty headers —
    // the bug this fixes. Segmented x:Uid names map "." → "/" (MRT Core
    // convention), so a card's "<Uid>.Header" entry is read as "<Uid>/Header".
    // The header is mandatory (a miss surfaces a DEBUG marker); the description is
    // optional (a miss leaves the card compact).
    private string ResolveHeader(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Header") : Loc.GetFrom(_module, $"{labelKey}/Header");

    private string? ResolveDescription(string labelKey)
        => _module is null ? null : Loc.GetFromOptional(_module, $"{labelKey}/Description");

    // A Choice option's label comes from the same .resw entry its ComboBoxItem's
    // x:Uid would read — "<key>.Content" (segmented "." → "/"), in the module's
    // PRI subtree. Same module-aware resolution as the header; only the suffix
    // differs (a card carries ".Header", a content control carries ".Content").
    private string ResolveOptionLabel(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Content") : Loc.GetFrom(_module, $"{labelKey}/Content");

    // The descriptor carries the glyph character itself (from Glyphs.*, the C#
    // mirror of Icons.xaml that exists precisely for programmatic FontIcons), so
    // the icon is built directly with no resource lookup. A code-side
    // Application.Resources[key] is avoided on purpose: it does not walk
    // Theme/Merged dictionaries reliably (see Deckle.Hud HudMessage), and Glyphs
    // is the blessed path here.
    private static IconElement? BuildIcon(string? glyph)
        => string.IsNullOrEmpty(glyph) ? null : new FontIcon { Glyph = glyph };

    private static bool AsBool(object? value) => value is bool b && b;
    private static double AsDouble(object? value) => value is double d ? d : 0d;
    private static string AsString(object? value) => value as string ?? string.Empty;

    // The secondary text brush for the slider's value/unit readouts — the same
    // {ThemeResource TextFillColorSecondaryBrush} the XAML rows use, fetched from
    // the application root where this framework theme key lives. This is a
    // top-level theme brush (unlike the icon brushes the BuildIcon note warns
    // about), so the root-dictionary lookup is the supported path; the other
    // consent dialogs resolve their styles the same way.
    private static Microsoft.UI.Xaml.Media.Brush? SecondaryBrush()
        => Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush;
}
