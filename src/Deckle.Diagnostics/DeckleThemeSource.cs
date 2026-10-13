using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: light / dark theme transitions on the app's
// XAML surfaces. Without this event, a rendering glitch correlated with a
// system switch (the OS changes Personalization > Color, the user clicks
// Appearance in Settings, the app forces RequestedTheme at boot) leaves no
// systematic trace: a brush that does not reapply, a foreground left as a
// hardcoded color, a caption button stuck in the old theme, all are diagnosed
// by looking for the transition in the log. The primitive is strictly
// non-business (XAML platform wiring) and consumed by all app windows with
// the same parameter set.
//
// Closed `surface` vocabulary (short logical name, to extend here if a new
// window is added to the project):
//   "hud"         — HudWindow (bottom-center, long-lived)
//   "hud-overlay" — HudOverlayWindow (stacked transient card)
//   "settings"    — SettingsWindow
//   "log"         — LogWindow
//   "setup"       — SetupWindow first-run wizard
//   "playground"  — PlaygroundWindow
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
//
// Closed `source` vocabulary, inferred through ThemeRequestSourceProbe:
//   "system"   — the OS changed the system theme (Windows Settings >
//                Personalization > Colors > Choose your mode). This is the
//                default case when the origin cannot be distinguished.
//   "user"     — the user changed the theme through Deckle's Settings page
//                (Appearance combo), which forces `RequestedTheme` on each
//                window through `App.ApplyTheme`.
//   "app-init" — the app sets the initial theme at boot (first `ApplyTheme`
//                after reading settings, before the first useful render
//                frame).
[EventSource(Name = "Deckle-Theme")]
public sealed class DeckleThemeSource : DeckleEventSource
{
    public static readonly DeckleThemeSource Log = new();

    private DeckleThemeSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtThemeChanged = 1;

    // Emitted by each `FrameworkElement.ActualThemeChanged` handler wired on
    // the XAML root of a Deckle window. Verbose because it carries identifiers
    // (theme name, surface name).
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
// null. When the handler runs without a pending value, the OS moved and the
// caller applies the `"system"` fallback. If the app sets RequestedTheme and
// an OS change lands in the same dispatcher tick, a system transition is
// labelled with the app's source.
//
// No lock: every Push and Consume runs on the UI thread, where XAML theme
// changes are marshalled by the framework.
//
// No stack: a second Push before the handler runs overwrites the first; the
// latest writer is the one responsible at fire time.
public static class ThemeRequestSourceProbe
{
    private static string? _pending;

    // Call just before each write to `FrameworkElement.RequestedTheme` or
    // `AppWindow.TitleBar.PreferredTheme` that may trigger an
    // ActualThemeChanged.
    public static void Push(string source) => _pending = source;

    // `null` means no Push preceded the ActualThemeChanged handler.
    public static string? Consume()
    {
        var s = _pending;
        _pending = null;
        return s;
    }
}
