using System;
using System.Collections.Generic;

namespace Deckle.Catalog;

// ── SettingsNavigation ────────────────────────────────────────────────────────
//
// The hooks a settings page uses to know where it sits and to send the user to
// another settings page. A module page cannot see Deckle.Settings (the
// dependency runs the other way), so the hooks live on the floor both sides
// reference.
//
// GoToPage takes the destination's PageTag, the same Type.GetType string the
// nav item carries. OpenSubpage opens a page the rail does not list, one level
// under the page on screen (one profile of the Rewriting page), as Windows 11
// Settings drills into a device or a network: the rail keeps its selection, the
// title bar shows its back button and the header names the way back. The
// parameter is handed to the page's OnNavigatedTo. The hooks stay null when
// unwired (an isolated module test, a page hosted outside the Settings window):
// the card then does nothing and the header shows its own title.
public static class SettingsNavigation
{
    public static Action<string>? GoToPage { get; set; }

    public static Action<Type, object?>? OpenSubpage { get; set; }

    public static Func<Type, IReadOnlyList<SettingsBreadcrumbSegment>?>? BreadcrumbFor { get; set; }
}

// One segment of a page's path in the rail: its label as the rail prints it,
// and the tag of the page it leads to, null for the segment of the page itself.
public sealed record SettingsBreadcrumbSegment(string Label, string? PageTag);
