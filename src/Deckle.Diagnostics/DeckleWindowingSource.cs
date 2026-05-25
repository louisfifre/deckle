using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Sub-provider transverse — positionnement et dimensionnement de toute
// fenêtre WinUI 3 ou Win32 de l'app (HUD, HudOverlay, tray popup,
// SettingsWindow, LogWindow, SetupWindow, FolderPicker). Sans cet event
// transverse, un bug de placement DPI ou multi-écran n'a aucune trace —
// l'instrumentation se ferait à la main avec `File.AppendAllText` au
// site exact, exactement le chemin parallèle que la doctrine de
// centralisation veut éviter. La primitive est strictement non-métier
// (un wiring de plateforme) et consommée par plusieurs modules avec
// exactement le même set de paramètres — promotion en sub-provider
// transverse au sens du critère à deux clauses de la fiche
// `reference--eventsource-convention--1.2.md` §*Sub-providers
// transverses*.
//
// Convention de coordonnées : pixels écran absolus partout. Les calculs
// internes peuvent partir de DIP, mais les events portent toujours du
// pixel pour permettre la reverse via `dpi`. Cohérent avec ce que
// retournent `GetCursorPos`, `GetWindowRect`, `GetMonitorInfo`. Cf.
// 1.2 §*Classe 6 — Windowing* pour le set canonique de paramètres.
//
// Pattern « tronc commun + events spécialisés ». `WindowPositioned` est
// le tronc émis par tout site qui positionne ou redimensionne une
// fenêtre. Les overlays empilés émettent EN PLUS `OverlaySlotAssigned`
// (le slot ne ferait pas sens sur les fenêtres app). Les popups ancrés
// à un contrôle parent émettent EN PLUS `PopupAnchored` (avec le rect
// du contrôle ancré sérialisé en string "x,y,w,h" pour tenir dans 6
// paramètres EventSource).
//
// Vocabulaire fermé `window` (nom logique court pour le tronc commun) :
//   "hud"           — fenêtre principale HudWindow (bas-centre)
//   "hud-overlay"   — carte transient empilée HudOverlayWindow
//   "settings"      — SettingsWindow
//   "log"           — LogWindow
//   "setup"         — SetupWindow first-run wizard
//   "tray-popup"    — popup contextuel du tray icon
//   "folder-picker" — picker FolderPicker système ouvert depuis Settings
// Toute nouvelle fenêtre ajoutée au projet doit étendre ce vocabulaire
// avant émission, pour préserver la grep-abilité côté listener.
//
// Vocabulaire fermé `anchor` (intention de placement côté code, pas une
// mesure) :
//   "BottomCenter"    — HUD en mode BottomCenter (default)
//   "TopCenter"       — HUD en mode TopCenter
//   "Center"          — fenêtre centrée sur la work area (Settings, Log,
//                       Setup)
//   "CursorRelative"  — placement relatif au curseur (tray popup)
//   "ParentRelative"  — placement relatif à un contrôle parent (folder
//                       picker)
//   "absolute"        — pas d'ancrage logique, juste un move/resize
//                       (placement Win32 brut)
[EventSource(Name = "Deckle.Diagnostics.Windowing")]
public sealed class DeckleWindowingSource : DeckleEventSource
{
    public static readonly DeckleWindowingSource Log = new();

    private DeckleWindowingSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtWindowPositioned     = 1;
    public const int EvtOverlaySlotAssigned  = 2;
    public const int EvtPopupAnchored        = 3;

    // Tronc commun — émis par tout site qui positionne ou redimensionne
    // une fenêtre. `window` est un nom logique court (cf. vocabulaire
    // fermé ci-dessus). `hmon` est le handle moniteur retourné par
    // `MonitorFromWindow`, `dpi` vient de `GetDpiForWindow`, `anchor`
    // décrit l'ancrage choisi côté code, `pos`/`size` sont en pixels
    // écran absolus. Les overlays et popups émettent CET event en plus
    // de leur event spécialisé.
    [Event(EvtWindowPositioned,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "window positioned | window={0} | hmon=0x{1:X} | dpi={2} | anchor={3} | pos={4},{5} size={6},{7}")]
    public void WindowPositioned(
        string window, long hmon, int dpi, string anchor,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtWindowPositioned, window, hmon, dpi, anchor, pos_x, pos_y, size_w, size_h);
    }

    // Spécialisation overlays empilés — `slot=0` pour le plus proche du
    // HUD principal, `slot=1` pour le suivant, etc. `WindowPositioned`
    // est aussi émis avec window="hud-overlay" pour conserver le
    // déterminisme du tronc commun et permettre à un listener qui
    // s'abonnerait uniquement à `OverlaySlotAssigned` de ne pas recevoir
    // le bruit des fenêtres app non-overlay.
    [Event(EvtOverlaySlotAssigned,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "overlay slot | slot={0} | hmon=0x{1:X} | pos={2},{3} size={4},{5}")]
    public void OverlaySlotAssigned(
        int slot, long hmon,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtOverlaySlotAssigned, slot, hmon, pos_x, pos_y, size_w, size_h);
    }

    // Spécialisation popups ancrés — `parent_rect` est le rectangle du
    // contrôle ancré (ex. icône tray, bouton FolderPicker) en pixels
    // écran absolus, sérialisé en string "x,y,w,h" pour tenir dans 6
    // paramètres EventSource. `WindowPositioned` est aussi émis avec
    // window="tray-popup" ou "folder-picker" pour le tronc commun
    // quand le popup est une fenêtre que l'app possède ; les popups
    // dont l'app n'a pas le HWND (menu natif TrackPopupMenu, dialog
    // système FolderPicker) n'émettent que `PopupAnchored` avec ce
    // qu'on sait du déclencheur côté code.
    [Event(EvtPopupAnchored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "popup anchored | popup={0} | parent_rect={1} | pos={2},{3} size={4},{5}")]
    public void PopupAnchored(
        string popup, string parent_rect,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtPopupAnchored, popup, parent_rect, pos_x, pos_y, size_w, size_h);
    }
}
