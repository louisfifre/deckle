using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;

namespace Deckle.Diagnostics;

// Helper interne au module Diagnostics — factorise les P/Invoke Win32
// (`GetWindowRect`, `GetDpiForWindow`, `MonitorFromWindow`) consommées
// par les sept sites de positionnement de fenêtres câblés sur
// `DeckleWindowingSource`. Sans ce helper, chaque site dupliquerait les
// quatre lignes de P/Invoke + la construction des paramètres — sept
// sites multiplieraient la dette d'instrumentation par sept.
//
// **Pas de dépendance vers `Deckle.Core`.** Le module Diagnostics est
// sous toutes les autres briques techniques (cf. `CLAUDE.md`) ; les
// P/Invoke nécessaires sont redéclarées localement (privées) plutôt que
// d'introduire une dep dure sur `Deckle.Core.Interop.NativeMethods`.
// Aucun chevauchement de symbole — les déclarations P/Invoke sont
// locales à ce fichier, et `Deckle.Core` garde ses propres déclarations
// pour le reste de l'app.
//
// **Gate strict avant tout coût.** Chaque méthode `Emit*` teste
// `IsEnabled(Verbose, Windowing)` en tête : quand aucun listener
// n'écoute, l'instrumentation a un coût net nul (un test ETW + un
// retour). Les P/Invoke ne sont jamais appelées si le gate est fermé.
//
// **Convention pixels écran absolus.** `GetWindowRect` retourne déjà
// du pixel écran absolu (contrairement à `AppWindow.Position`/`Size`
// qui restent en pixel mais sont reliés à l'`AppWindow` post-Move).
// Lire directement le rect post-positionnement garantit qu'on capture
// l'état effectif côté DWM, pas l'intention pré-Move.
public static class WindowingProbe
{
    // ── P/Invoke privées ────────────────────────────────────────────────

    // Rect en pixels écran absolus de la fenêtre — inclut la zone non-
    // client (frame, titre) ; pour les fenêtres Deckle qui suppriment
    // la NC area via WM_NCCALCSIZE (HUD, HudOverlay) c'est équivalent
    // au rect client. Pour les fenêtres app classiques (Settings, Log,
    // Setup) l'écart NC est marginal (frame standard ~1 dip + caption).
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    // DPI logique de la fenêtre (96 = 100%, 120 = 125%, 144 = 150%…).
    // Per-monitor DPI aware : suit le moniteur sur lequel se trouve la
    // fenêtre, change runtime sur drag cross-monitor.
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Handle moniteur sur lequel se trouve la fenêtre. `dwFlags=2`
    // (MONITOR_DEFAULTTONEAREST) garantit qu'on a toujours un moniteur
    // même si la fenêtre est partiellement hors écran (cas runtime
    // après changement de résolution ou déconnexion d'un écran).
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    // ── Helpers d'émission ──────────────────────────────────────────────

    // Émet le tronc commun `WindowPositioned` pour tout site qui
    // positionne ou redimensionne une fenêtre dont l'app possède le
    // HWND. `window` est un nom logique court du vocabulaire fermé
    // (cf. doc `DeckleWindowingSource.WindowPositioned`), `anchor`
    // décrit l'intention de placement côté code.
    public static void EmitWindowPositioned(IntPtr hwnd, string window, string anchor)
    {
        if (!DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        if (hwnd == IntPtr.Zero) return;

        if (!GetWindowRect(hwnd, out var rect)) return;
        int dpi = (int)GetDpiForWindow(hwnd);
        long hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST).ToInt64();

        DeckleWindowingSource.Log.WindowPositioned(
            window, hmon, dpi, anchor,
            rect.left, rect.top,
            rect.right - rect.left, rect.bottom - rect.top);
    }

    // Émet `OverlaySlotAssigned` (spécialisé empilement) en plus du
    // tronc commun déjà émis par EmitWindowPositioned. Les overlays
    // Deckle (`HudOverlayWindow`) ont chacun un slot 0..N-1 assigné
    // par `HudOverlayManager.Recompact` — slot 0 = plus proche du HUD
    // principal, slot N-1 = le plus éloigné.
    public static void EmitOverlaySlotAssigned(IntPtr hwnd, int slot)
    {
        if (!DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        if (hwnd == IntPtr.Zero) return;

        if (!GetWindowRect(hwnd, out var rect)) return;
        long hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST).ToInt64();

        DeckleWindowingSource.Log.OverlaySlotAssigned(
            slot, hmon,
            rect.left, rect.top,
            rect.right - rect.left, rect.bottom - rect.top);
    }

    // Émet `PopupAnchored` (spécialisé ancrage parent) en plus du
    // tronc commun. `popup` est un nom logique court ("tray-popup",
    // "folder-picker"), `parent_rect_x/y/w/h` décrivent le rect du
    // contrôle ancré (icône tray, bouton picker) en pixels écran
    // absolus. Pour les popups dont l'app n'a pas le HWND (menu natif
    // TrackPopupMenu, dialog système FolderPicker), passer
    // `IntPtr.Zero` en hwnd — la position/taille effective est alors
    // émise comme zéro et seule l'intention (parent_rect) est tracée.
    public static void EmitPopupAnchored(
        IntPtr hwnd, string popup,
        int parent_rect_x, int parent_rect_y,
        int parent_rect_w, int parent_rect_h)
    {
        if (!DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;

        int pos_x = 0, pos_y = 0, size_w = 0, size_h = 0;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect))
        {
            pos_x  = rect.left;
            pos_y  = rect.top;
            size_w = rect.right - rect.left;
            size_h = rect.bottom - rect.top;
        }

        string parent_rect = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{parent_rect_x},{parent_rect_y},{parent_rect_w},{parent_rect_h}");

        DeckleWindowingSource.Log.PopupAnchored(
            popup, parent_rect, pos_x, pos_y, size_w, size_h);
    }
}
