using System;

namespace Deckle.Catalog;

// ── SettingsNavigation ────────────────────────────────────────────────────────
//
// Lets a settings page send the user to another settings page. A module page
// cannot see Deckle.Settings (the dependency runs the other way), so the hook
// lives here and the App wires it at boot. The argument is the destination's
// PageTag. Unwired (a module test, a page hosted outside the Settings window),
// the call does nothing.
public static class SettingsNavigation
{
    public static Action<string>? GoToPage { get; set; }
}
