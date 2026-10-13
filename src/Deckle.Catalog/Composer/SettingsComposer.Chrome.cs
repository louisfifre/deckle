using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // ── Per-card reset ─────────────────────────────────────────────────────────
    //
    // Wraps a value control with an inline reset when its descriptor carries a
    // Default, and registers the row in the reset surface. Returns the card's
    // Content and an UpdateState closure the row's refresher must call: it enables
    // the button only while the value differs from its default. With no Default
    // the control comes back unchanged and UpdateState is null.
    //
    // The button sits left of the value control, so the value stays on the
    // trailing edge where the eye expects it.
    private (FrameworkElement Content, Action? UpdateState) WrapWithReset(
        SettingsCard card, FrameworkElement valueControl, SettingDescriptor s)
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
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(reset);
        content.Children.Add(valueControl);

        string tooltip = ResolveResetTooltip();
        void UpdateState()
        {
            reset.IsEnabled = Dirty();
            ToolTipService.SetToolTip(reset, tooltip);
        }

        return (content, UpdateState);
    }

    // Matches the hand-authored ResetButtonStyle (WhisperPage, AmbientPage):
    // Opacity 0 at rest until WireReveal shows it.
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

    // Pointer over the host or keyboard focus on the button shows it; tracking both
    // means leaving one while the other holds does not hide it early. Opacity, not
    // Visibility, so the button keeps its slot and the reveal never reflows the row.
    private static void WireReveal(FrameworkElement host, Button button)
    {
        bool pointerOver = false, focused = false;
        void Update() => button.Opacity = pointerOver || focused ? 1 : 0;
        host.PointerEntered += (_, _) => { pointerOver = true; Update(); };
        host.PointerExited += (_, _) => { pointerOver = false; Update(); };
        button.GotFocus += (_, _) => { focused = true; Update(); };
        button.LostFocus += (_, _) => { focused = false; Update(); };
    }

    // Composer-owned and identical for every module, so it resolves from the host
    // app's root map, not the module .resw that headers come from.
    private string ResolveResetTooltip()
        => Loc.Get("SettingsComposer_ResetToDefault");

    private static void ApplyReactiveState(Control control, SettingDescriptor s)
    {
        if (s.EnabledWhen is not null) control.IsEnabled = s.EnabledWhen();
        if (s.VisibleWhen is not null)
            control.Visibility = s.VisibleWhen() ? Visibility.Visible : Visibility.Collapsed;
    }

    // Resolves from the owning module's PRI subtree, the entries an x:Uid would
    // read. A code-created element gets no x:Uid resolution, and a module's strings
    // are absent from the app root map, so Loc.Get would yield empty headers.
    // Segmented x:Uid names map "." to "/" (MRT Core), so "<Uid>.Header" is read as
    // "<Uid>/Header". A missing header shows a DEBUG marker; the description is
    // optional.
    private string ResolveHeader(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Header") : Loc.GetFrom(_module, $"{labelKey}/Header");

    private string? ResolveDescription(string labelKey)
        => _module is null ? null : Loc.GetFromOptional(_module, $"{labelKey}/Description");

    // The "<key>.Content" entry a ComboBoxItem's x:Uid would read, resolved like
    // the header.
    private string ResolveOptionLabel(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Content") : Loc.GetFrom(_module, $"{labelKey}/Content");

    // The descriptor carries the glyph character itself (Glyphs.*), so the icon
    // needs no resource lookup.
    private static IconElement? BuildIcon(string? glyph)
        => string.IsNullOrEmpty(glyph) ? null : new FontIcon { Glyph = glyph };

    private static bool AsBool(object? value) => value is bool b && b;
    private static double AsDouble(object? value) => value is double d ? d : 0d;
    private static string AsString(object? value) => value as string ?? string.Empty;

    // The TextFillColorSecondaryBrush the XAML rows use for unit readouts.
    private static Microsoft.UI.Xaml.Media.Brush? SecondaryBrush()
        => Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush;
}
