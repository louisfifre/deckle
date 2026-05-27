using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

// ─── Tray switch menu item — composite réutilisable ───────────────────────────
//
// Helper de création et de pilotage d'état pour un MenuFlyoutItem présentant
// une pillule on/off à droite du libellé. Encapsule le wiring qu'il faudrait
// sinon répéter à chaque item togglable du tray menu : application du Style
// ToggleSwitchMenuItemStyle (Themes/TrayMenu.xaml) et bascule des états
// visuels On/Off via VisualStateManager.GoToState.
//
// Le Style fournit une pillule dessinée à la main (Border arrondi + Ellipse),
// pas un ToggleSwitch natif — le contrôle WinUI 3 est pensé pour vivre dans
// une SettingsCard et ne se laisse pas centrer proprement greffé dans un
// MenuFlyoutItem. La pillule custom suit les ThemeResource Win11
// (ToggleSwitchFillOn/Off, ToggleSwitchStrokeOn/Off, ToggleSwitchKnobFillOn/Off)
// pour rester alignée light/dark/contrast/accent.
//
// Convention d'usage côté hôte : Create() au build du flyout, SetState() avant
// chaque ouverture pour refléter l'état applicatif courant. Doit être appelé
// sur le thread UI (accède à Application.Current.Resources).

public static class TraySwitchMenuItem
{
    /// <summary>
    /// Crée un MenuFlyoutItem stylé avec une pillule on/off à droite du libellé.
    /// </summary>
    /// <param name="text">Libellé affiché à gauche de la pillule.</param>
    /// <param name="onActivate">Callback invoquée au Click sur l'item (le Hide
    /// du flyout côté hôte reste à la charge de l'appelant).</param>
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
    /// Bascule l'état visuel de la pillule (On = knob à droite + rail accent,
    /// Off = knob à gauche + rail neutre). À appeler avant chaque ouverture du
    /// flyout pour synchroniser l'affichage avec l'état applicatif.
    /// useTransitions=false : aligné sur AreOpenCloseAnimationsEnabled=false
    /// du flyout, le menu apparaît et disparaît instantanément.
    /// </summary>
    public static void SetState(MenuFlyoutItem item, bool isOn)
    {
        VisualStateManager.GoToState(item, isOn ? "On" : "Off", useTransitions: false);
    }
}
