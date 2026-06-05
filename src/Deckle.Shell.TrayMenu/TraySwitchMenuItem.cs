using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

// ─── Tray switch menu item — reusable composite ───────────────────────────────
//
// Creation and state-driving helper for a MenuFlyoutItem presenting an on/off
// pill to the right of the label. Encapsulates the wiring that would otherwise
// be repeated for each togglable tray menu item: applying
// ToggleSwitchMenuItemStyle (Themes/TrayMenu.xaml) and switching On/Off visual
// states through VisualStateManager.GoToState.
//
// The Style provides a hand-drawn pill (rounded Border + Ellipse), not a native
// ToggleSwitch. The WinUI 3 control is designed to live in a SettingsCard and
// cannot be centered cleanly when grafted into a MenuFlyoutItem. The custom
// pill follows Win11 ThemeResources (ToggleSwitchFillOn/Off,
// ToggleSwitchStrokeOn/Off, ToggleSwitchKnobFillOn/Off) to stay aligned with
// light/dark/contrast/accent.
//
// Host-side usage convention: Create() when building the flyout, SetState()
// before each open to reflect current application state. Must be called on the
// UI thread (accesses Application.Current.Resources).

public static class TraySwitchMenuItem
{
    /// <summary>
    /// Creates a styled MenuFlyoutItem with an on/off pill to the right of the label.
    /// </summary>
    /// <param name="text">Label displayed to the left of the pill.</param>
    /// <param name="onActivate">Callback invoked on item Click (flyout Hide on
    /// the host side remains the caller's responsibility).</param>
    public static MenuFlyoutItem Create(string text, Action onActivate)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Style = (Style)Application.Current.Resources["ToggleSwitchMenuItemStyle"],
        };
        item.Click += (_, _) => onActivate();
        return item;
    }

    /// <summary>
    /// Switches the pill visual state (On = knob right + accent rail,
    /// Off = knob left + neutral rail). Call before each flyout open to
    /// synchronize display with application state. useTransitions=false:
    /// aligned with the flyout's AreOpenCloseAnimationsEnabled=false, so the
    /// menu appears and disappears instantly.
    /// </summary>
    public static void SetState(MenuFlyoutItem item, bool isOn)
    {
        VisualStateManager.GoToState(item, isOn ? "On" : "Off", useTransitions: false);
    }
}
