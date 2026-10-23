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
           Message = "surface={0} | from={1} | to={2} | source={3}")]
    public void ThemeChanged(string surface, string from, string to, string source)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Theme)) return;
        WriteEvent(EvtThemeChanged, surface, from, to, source);
    }
}

// Static probe consumed by `ActualThemeChanged` handlers to recover the origin
// of a switch. Code that *triggers* a programmatic switch (App.ApplyTheme)
// calls `Push(source)` just before writing `RequestedTheme` on its windows;
// each window's handler calls `Consume()`. When it returns null, no request
// is recent, the OS moved, and the caller applies the `"system"` fallback.
//
// The source stays valid for RequestWindowMs after the Push rather than being
// cleared by the first handler: one request re-themes every open window, and
// their handlers may run after the whole loop of writes, so a read-once value
// labelled the first window and left the others as "system". The span also
// bounds staleness: a push no handler read (the window was already in that
// theme) must not label a later OS change. An OS change that lands inside the
// span is still labelled with the app's source.
//
// No lock: every Push and Consume runs on the UI thread, where XAML theme
// changes are marshalled by the framework.
//
// No stack: a second Push overwrites the first and restarts the span; the
// latest writer is the one responsible at fire time.
public static class ThemeRequestSourceProbe
{
    // Long enough for the handlers of every window to run after a burst of
    // RequestedTheme writes, short against how often a person flips the OS
    // theme.
    internal const long RequestWindowMs = 2000;

    private static string? _pending;
    private static long _pushedAtMs;

    // Call just before each write to `FrameworkElement.RequestedTheme` or
    // `AppWindow.TitleBar.PreferredTheme` that may trigger an
    // ActualThemeChanged.
    public static void Push(string source) => Push(source, Environment.TickCount64);

    internal static void Push(string source, long nowMs)
    {
        _pending = source;
        _pushedAtMs = nowMs;
    }

    // `null` means no Push within RequestWindowMs before the handler ran. The
    // name predates the span: the value is read, not consumed, and the name
    // stays because six window handlers outside this module call it.
    public static string? Consume() => Consume(Environment.TickCount64);

    internal static string? Consume(long nowMs)
    {
        if (_pending is null) return null;
        if (nowMs - _pushedAtMs <= RequestWindowMs) return _pending;
        _pending = null;
        return null;
    }
}
