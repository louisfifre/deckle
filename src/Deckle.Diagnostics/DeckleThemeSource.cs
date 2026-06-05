using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: theme transitions (light / dark /
// HighContrast / accent) on the app's XAML surfaces. Without this
// cross-cutting event, a rendering glitch correlated with a system switch
// (the OS changes Personalization > Color, the user clicks Appearance in
// Settings, the app forces RequestedTheme at boot) leaves no systematic trace:
// a brush that does not reapply, a foreground left as a hardcoded color, a
// caption button stuck in the old theme, all are diagnosed by looking for the
// transition in the log. The primitive is strictly non-business (XAML platform
// wiring) and consumed by all app windows with the same parameter set:
// promotion to cross-cutting sub-provider under the two-clause criterion in
// `reference--eventsource-convention--1.2.md` §*Cross-cutting sub-providers*.
//
// Closed `surface` vocabulary (short logical name, to extend here if a new
// window is added to the project):
//   "hud"         — HudWindow (bottom-center, long-lived)
//   "hud-overlay" — HudOverlayWindow (stacked transient card)
//   "settings"    — SettingsWindow
//   "log"         — LogWindow
//   "setup"       — SetupWindow first-run wizard
//   "tray"        — TrayIconManager (notification icon + Win32 menu)
//
// Closed `from` / `to` vocabulary: string representations of
// `Microsoft.UI.Xaml.ElementTheme`:
//   "Light"        — light palette
//   "Dark"         — dark palette
//   "Default"      — follow system theme (never observed directly from
//                    ActualThemeChanged, which always resolves to Light or
//                    Dark; only present in `from` when the app moves from a
//                    not-yet-materialized "follow system" state to a concrete
//                    value)
//   "HighContrast" — placeholder if a future High Contrast switch is observed
//                    distinctly (ElementTheme does not expose HighContrast in
//                    V1; it is carried by theme resources and
//                    `ApplicationHighContrastAdjustment`)
//
// Closed `source` vocabulary: transition trigger as can be inferred from the
// code side. Imperfect heuristic by design (see note below):
//   "system"   — the OS changed the system theme (Windows Settings >
//                Personalization > Colors > Choose your mode). This is the
//                default case when we cannot distinguish.
//   "user"     — the user changed the theme through Deckle's Settings page
//                (Appearance combo), which forces `RequestedTheme` on each
//                window through `App.ApplyTheme`.
//   "app-init" — the app sets the initial theme at boot (first `ApplyTheme`
//                after reading settings, before the first useful render
//                frame).
//
// Heuristic to distinguish `source` in the ActualThemeChanged handler: keep a
// static `_pendingSource` field set just before explicit `RequestedTheme`
// assignments (on the App.ApplyTheme side) and consumed by the handler through
// `RequestSourceProbe.Consume()`. When the handler runs without a pending
// value, the OS moved: fallback "system". This heuristic can be wrong if the
// app sets RequestedTheme and an OS change arrives in the same dispatcher tick
// (rare race; worst case is a system transition labeled "user"). The
// distinction remains useful in ordinary reading.
[EventSource(Name = "Deckle-Theme")]
public sealed class DeckleThemeSource : DeckleEventSource
{
    public static readonly DeckleThemeSource Log = new();

    private DeckleThemeSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtThemeChanged = 1;

    // Emitted by each `FrameworkElement.ActualThemeChanged` handler wired on
    // the XAML root of a Deckle window, or by any site that can detect a theme
    // change on a non-XAML surface (tray icon). Verbose because values carry
    // identifiers (theme name, surface name) and grep-ability goes through
    // typed parameters rather than level, per the "any event carrying an ID is
    // Verbose" doctrine contract in Deckle.Diagnostics CLAUDE.md.
    [Event(EvtThemeChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Theme,
           Message = "theme changed | surface={0} | from={1} | to={2} | source={3}")]
    public void ThemeChanged(string surface, string from, string to, string source)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Theme)) return;
        WriteEvent(EvtThemeChanged, surface, from, to, source);
    }
}

// Static probe consumed by `ActualThemeChanged` handlers to recover the origin
// of a switch. Code that *triggers* a programmatic switch (App.ApplyTheme)
// calls `Push(source)` just before writing `RequestedTheme` on its windows; the
// handler calls `Consume()`, which returns the pending value and resets it to
// null. When the handler runs without a pending value, the `"system"` fallback
// is applied by the caller: this is the signature of an OS-initiated change.
//
// A static variable shared by all threads is acceptable here because all
// operations live on the UI thread (XAML theme changes are marshalled by the
// framework). No lock required.
//
// No stack: doctrine accepts that the `Push` immediately preceding the
// `RequestedTheme` write is consumed by the following `ActualThemeChanged`
// batch. A second Push before the batch overwrites the previous one; this is
// the right behavior because the second set is the one the user or app is
// responsible for at fire time.
public static class ThemeRequestSourceProbe
{
    private static string? _pending;

    // Marks the source of the next observable transition. Call just before
    // each write to `FrameworkElement.RequestedTheme` or
    // `AppWindow.TitleBar.PreferredTheme` that may trigger an
    // ActualThemeChanged.
    public static void Push(string source) => _pending = source;

    // Reads the pending source and resets it. The ActualThemeChanged handler
    // calls this on the first line; a `null` return means no Push preceded it,
    // so the caller must apply its fallback (typically "system" for XAML
    // surfaces, "system" for the tray icon).
    public static string? Consume()
    {
        var s = _pending;
        _pending = null;
        return s;
    }
}
