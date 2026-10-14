// TraySwitchMenuItem — a menu item with an on/off pill at its right edge.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

// ─── Tray switch menu item ────────────────────────────────────────────────────
//
// A MenuFlyoutItem whose template adds a small on/off pill to the right of the
// label (Themes/Generic.xaml). It is the row a switchable module takes in the
// left-click menu, in place of ToggleMenuFlyoutItem: that control reserves a
// check-mark column in the presenter, which shifts every other item of the
// menu to the right and leaves the reader unable to tell a switch from a
// command. The pill sits on the far side of the label, pushes nothing, and
// reads as a switch at a glance (Louis, 2026-05 and 2026-10-14).
//
// Hand-drawn pill, not a native ToggleSwitch: the WinUI control is built for a
// 32 DIP SettingsCard cell and cannot be vertically centered once grafted into
// a menu item. The pill's brushes are the ToggleSwitch theme resources, so it
// follows light, dark, contrast and accent like the real control.
//
// Click behavior is the item's own: a click closes the menu, then the host
// flips the module. The state is a dependency property so the host can set it
// before the first open, when the template is not applied yet: OnApplyTemplate
// replays it once the visual states exist.

public sealed class TraySwitchMenuItem : MenuFlyoutItem
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(TraySwitchMenuItem),
        new PropertyMetadata(false, OnIsOnChanged));

    /// <summary>The module state the pill shows. Set before each open.</summary>
    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public TraySwitchMenuItem()
    {
        DefaultStyleKey = typeof(TraySwitchMenuItem);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyState();
    }

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TraySwitchMenuItem)d).ApplyState();

    // No transition: the menu opens and closes without animation
    // (AreOpenCloseAnimationsEnabled = false), the pill appears in its state.
    private void ApplyState()
        => VisualStateManager.GoToState(this, IsOn ? "On" : "Off", useTransitions: false);
}
