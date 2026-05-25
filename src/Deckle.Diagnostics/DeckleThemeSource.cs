using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Sub-provider transverse — transitions de thème (light / dark /
// HighContrast / accent) sur les surfaces XAML de l'app. Sans cet event
// transverse, un glitch de rendu corrélé à une bascule système (l'OS
// change Personalization > Color, l'utilisateur clique sur Appearance
// dans Settings, l'app force RequestedTheme au boot) ne laisse aucune
// trace systématique — un brush qui ne se ré-applique pas, un foreground
// resté en hardcoded color, une caption button stuck dans l'ancien
// thème, tout se diagnostique en cherchant la transition dans le log.
// La primitive est strictement non-métier (un wiring de plateforme XAML)
// et consommée par toutes les fenêtres de l'app avec le même set de
// paramètres — promotion en sub-provider transverse au sens du critère
// à deux clauses de la fiche `reference--eventsource-convention--1.2.md`
// §*Sub-providers transverses*.
//
// Vocabulaire fermé `surface` (nom logique court, à étendre ici si une
// nouvelle fenêtre s'ajoute au projet) :
//   "hud"         — HudWindow (bas-centre, longue vie)
//   "hud-overlay" — HudOverlayWindow (carte transient empilée)
//   "settings"    — SettingsWindow
//   "log"         — LogWindow
//   "setup"       — SetupWindow first-run wizard
//   "tray"        — TrayIconManager (icône notification + menu Win32)
//
// Vocabulaire fermé `from` / `to` — représentations string de
// `Microsoft.UI.Xaml.ElementTheme` :
//   "Light"        — palette claire
//   "Dark"         — palette sombre
//   "Default"      — suivre le thème système (jamais observé directement
//                    en sortie d'ActualThemeChanged qui résout toujours
//                    en Light ou Dark, présent uniquement en `from` quand
//                    l'app passe d'un "follow system" non encore matérialisé
//                    à une valeur concrète)
//   "HighContrast" — placeholder si une future bascule High Contrast est
//                    observée distinctement (ElementTheme n'expose pas
//                    HighContrast en V1, c'est porté par les theme resources
//                    et `ApplicationHighContrastAdjustment`)
//
// Vocabulaire fermé `source` — déclencheur de la transition tel qu'on
// peut le déduire côté code. Heuristique imparfaite par construction
// (cf. note ci-dessous) :
//   "system"   — l'OS a changé le thème système (Windows Settings >
//                Personalization > Colors > Choose your mode). C'est le
//                cas par défaut quand on ne sait pas distinguer.
//   "user"     — l'utilisateur a changé le thème via la page Settings
//                de Deckle (Appearance combo) qui force `RequestedTheme`
//                sur chaque fenêtre via `App.ApplyTheme`.
//   "app-init" — l'app pose le thème initial au boot (premier `ApplyTheme`
//                après lecture des settings, avant la première render
//                frame utile).
//
// Heuristique pour distinguer `source` côté handler ActualThemeChanged :
// on entretient un champ statique `_pendingSource` posé juste avant les
// affectations explicites de `RequestedTheme` (côté App.ApplyTheme) et
// consommé par le handler via `RequestSourceProbe.Consume()`. Quand le
// handler tombe sans pending pose, c'est l'OS qui a bougé — fallback
// "system". Cette heuristique peut se tromper si l'app pose RequestedTheme
// et qu'un changement OS arrive dans la même tick de dispatcher (race
// rare ; le pire effet est qu'un transition system serait étiquetée
// "user"). La distinction reste utile à la lecture courante.
[EventSource(Name = "Deckle.Diagnostics.Theme")]
public sealed class DeckleThemeSource : DeckleEventSource
{
    public static readonly DeckleThemeSource Log = new();

    private DeckleThemeSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtThemeChanged = 1;

    // Émis par chaque handler `FrameworkElement.ActualThemeChanged` câblé
    // sur la racine XAML d'une fenêtre Deckle, ou par tout site qui sait
    // détecter un changement de thème côté surface non-XAML (tray icon).
    // Verbose parce que les valeurs portent des identifiants (theme name,
    // surface name) et que la grep-abilité passe par les paramètres typés
    // plutôt que par le niveau, conformément au contrat doctrinal « tout
    // event qui porte un ID est Verbose » du CLAUDE.md Deckle.Diagnostics.
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

// Probe statique consommée par les handlers `ActualThemeChanged` pour
// retrouver l'origine d'une bascule. Le code qui *déclenche* une bascule
// programmatique (App.ApplyTheme) appelle `Push(source)` juste avant
// d'écrire `RequestedTheme` sur ses fenêtres ; le handler appelle
// `Consume()` qui retourne le pending et le reset à null. Quand le
// handler tombe sans pending, le fallback `"system"` est appliqué côté
// appelant — c'est la signature d'un changement initié par l'OS.
//
// Une variable statique partagée par tous les threads est admissible
// ici parce que toutes les opérations vivent sur le UI thread (XAML
// theme changes sont marshalés par le framework). Pas de lock requis.
//
// Pas de pile : la doctrine accepte que `Push` qui précède immédiatement
// l'écriture `RequestedTheme` soit consommée par le batch de
// `ActualThemeChanged` qui suit. Un second Push avant le batch écrase
// le précédent — c'est le bon comportement parce que le second pose est
// celui dont l'utilisateur ou l'app est responsable au moment du fire.
public static class ThemeRequestSourceProbe
{
    private static string? _pending;

    // Marque la source de la prochaine transition observable. À appeler
    // juste avant chaque écriture de `FrameworkElement.RequestedTheme`
    // ou `AppWindow.TitleBar.PreferredTheme` qui pourrait déclencher un
    // ActualThemeChanged.
    public static void Push(string source) => _pending = source;

    // Lit la source pending et la reset. Le handler ActualThemeChanged
    // appelle ceci en première ligne ; le retour `null` signale qu'aucun
    // Push n'a précédé — l'appelant doit appliquer son fallback (typiquement
    // "system" pour les surfaces XAML, "system" pour le tray icon).
    public static string? Consume()
    {
        var s = _pending;
        _pending = null;
        return s;
    }
}
