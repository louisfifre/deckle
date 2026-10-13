using System;
using System.Collections.Generic;

namespace Deckle.Catalog;

// ── SettingsNavigation ────────────────────────────────────────────────────────
//
// The hooks a settings page uses to know where it sits and to send the user to
// ANOTHER settings page, without a reference back to the shell. A module page
// cannot see Deckle.Settings (the dependency runs the other way), so the
// capability lands here on the floor both sides already reference, in the same
// lib-exposes-a-delegate / App-wires-it shape as SettingsComposer.
// PathControlFactory beside it.
//
// GoToPage takes the destination's PageTag — the same Type.GetType string the
// nav item carries — so a page names where it is going in the vocabulary the
// registry already uses. BreadcrumbFor answers the page header's question "what
// is my path?" from the shell's rail: the module, then the page, each segment
// carrying the tag the header navigates to when it is clicked (null on the
// current page). Both are null-safe by construction: unwired (an isolated module
// test, a page hosted outside the Settings window) the card does nothing and the
// header shows its own title.
public static class SettingsNavigation
{
    public static Action<string>? GoToPage { get; set; }

    public static Func<Type, IReadOnlyList<SettingsBreadcrumbSegment>?>? BreadcrumbFor { get; set; }
}

// One segment of a page's path in the rail: its label as the rail prints it,
// and the tag of the page it leads to, null for the segment of the page itself.
public sealed record SettingsBreadcrumbSegment(string Label, string? PageTag);
