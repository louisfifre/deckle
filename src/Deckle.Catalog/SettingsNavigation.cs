using System;

namespace Deckle.Catalog;

// ── SettingsNavigation ────────────────────────────────────────────────────────
//
// The one hook a settings page uses to send the user to ANOTHER settings page —
// the drill-in a navigation card needs, without a reference back to the shell.
// A module page cannot see Deckle.Settings (the dependency runs the other way),
// so the capability lands here on the floor both sides already reference, in the
// same lib-exposes-a-delegate / App-wires-it shape as SettingsComposer.
// PathControlFactory beside it.
//
// The argument is the destination's PageTag — the same Type.GetType string the
// nav item carries — so a page names where it is going in the vocabulary the
// registry already uses. Null-safe by construction: unwired (an isolated module
// test, a page hosted outside the Settings window) the card simply does nothing.
public static class SettingsNavigation
{
    public static Action<string>? GoToPage { get; set; }
}
