using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // ── Per-card reset ─────────────────────────────────────────────────────────
    //
    // Wraps a value control with an inline reset button when its descriptor
    // carries a Default; without one the control comes back unchanged and
    // updateState is null. The row's refresher must call updateState: it re-gates
    // the button off the live dirty-check, so a reset is offered only while the
    // value differs from its default. The button sits left of the value control,
    // so the value stays on the trailing edge where the eye expects it.
    //
    // describeDefault gives the default in the words the tooltip shows ("Reset to
    // 0.50"); null, or a null result, falls back to "Reset to default".
    private (FrameworkElement Content, Action? UpdateState) WrapWithReset(
        SettingsCard card, FrameworkElement valueControl, SettingDescriptor s,
        Func<string?>? describeDefault = null)
    {
        if (s.Default is null) return (valueControl, null);

        Button reset = BuildResetButton();

        WireReveal(card, reset);

        bool Dirty() => !DefaultEquals(s.GetValue(), s.Default!());
        _dirtyChecks.Add(Dirty);
        _resetActions.Add(() => s.SetValue(s.Default!()));

        reset.Click += (_, _) => s.SetValue(s.Default!());

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            // Matches the hand-authored per-card reset rows the composed cards sit
            // beside.
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(reset);
        content.Children.Add(valueControl);

        // Resolved once here, not per refresh, so the refresher never re-hits Loc.
        string tooltip = ResolveResetTooltip(describeDefault);
        void UpdateState()
        {
            reset.IsEnabled = Dirty();
            ToolTipService.SetToolTip(reset, tooltip);
        }

        return (content, UpdateState);
    }

    // Built to match the hand-authored per-card reset (WhisperPage's
    // ResetButtonStyle); keep the two in step.
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

    // Either the host's hover or the button's keyboard focus keeps it shown. A flat
    // Opacity flip: the button keeps its layout slot at rest, so the reveal never
    // reflows the row.
    private static void WireReveal(FrameworkElement host, Button button)
    {
        bool pointerOver = false, focused = false;
        void Update() => button.Opacity = pointerOver || focused ? 1 : 0;
        host.PointerEntered += (_, _) => { pointerOver = true; Update(); };
        host.PointerExited += (_, _) => { pointerOver = false; Update(); };
        button.GotFocus += (_, _) => { focused = true; Update(); };
        button.LostFocus += (_, _) => { focused = false; Update(); };
    }

    // The reset tooltip is composer-owned and identical for every module, so it
    // resolves from the host app's root map, not the module .resw. The value form
    // carries the default, which the description does not state; the generic form
    // serves a fold, whose reset restores several values.
    private string ResolveResetTooltip(Func<string?>? describeDefault = null)
    {
        string? value = describeDefault?.Invoke();
        return value is null
            ? Loc.Get("SettingsComposer_ResetToDefault")
            : Loc.Format("SettingsComposer_ResetTo", value);
    }

    private string DescribeToggleDefault(SettingDescriptor s)
        => Loc.Get(AsBool(s.Default!()) ? "SettingsComposer_ValueOn" : "SettingsComposer_ValueOff");

    private static void ApplyReactiveState(Control control, SettingDescriptor s)
    {
        if (s.EnabledWhen is not null) control.IsEnabled = s.EnabledWhen();
        if (s.VisibleWhen is not null)
            control.Visibility = s.VisibleWhen() ? Visibility.Visible : Visibility.Collapsed;
    }

    // Resolves from the owning module's PRI subtree: a code-created element gets
    // no x:Uid auto-resolution, and a module's strings are absent from the app
    // root map, so Loc.Get would silently yield empty headers. Segmented x:Uid
    // names map "." to "/" (MRT Core), so "<Uid>.Header" is read as
    // "<Uid>/Header". A missing header surfaces a DEBUG marker; a missing
    // description leaves the card compact.
    private string ResolveHeader(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Header") : Loc.GetFrom(_module, $"{labelKey}/Header");

    private string? ResolveDescription(string labelKey)
        => _module is null ? null : Loc.GetFromOptional(_module, $"{labelKey}/Description");

    // A Choice option reads the entry its ComboBoxItem's x:Uid would read,
    // "<key>.Content".
    private string ResolveOptionLabel(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Content") : Loc.GetFrom(_module, $"{labelKey}/Content");

    // The descriptor carries the glyph character itself, from Glyphs.*, so the
    // icon needs no resource lookup. A code-side Application.Resources[key] is
    // avoided on purpose: it does not walk Theme/Merged dictionaries reliably.
    private static IconElement? BuildIcon(string? glyph)
        => string.IsNullOrEmpty(glyph) ? null : new FontIcon { Glyph = glyph };

    private static bool AsBool(object? value) => value is bool b && b;
    private static double AsDouble(object? value) => value is double d ? d : 0d;
    private static string AsString(object? value) => value as string ?? string.Empty;

    // TextFillColorSecondaryBrush is a framework theme key that lives at the
    // application root, so this root lookup is the exception to the BuildIcon note.
    private static Microsoft.UI.Xaml.Media.Brush? SecondaryBrush()
        => Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush;
}
