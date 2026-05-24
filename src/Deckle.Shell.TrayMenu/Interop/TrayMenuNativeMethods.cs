using System.Runtime.InteropServices;
using Deckle.Core.Interop;

namespace Deckle.Shell.TrayMenu.Interop;

// ─── P/Invokes spécifiques au tray menu ───────────────────────────────────────
//
// Les imports génériques (GetCursorPos, SetForegroundWindow, ShowWindow,
// SetWindowLongPtr, SetLayeredWindowAttributes, DwmSetWindowAttribute,
// GetDpiForWindow…) vivent dans Deckle.Core.Interop.NativeMethods et sont
// consommés tels quels. Ici on ajoute uniquement ce qui manque : positionneur
// de popup natif et constantes de style associées.

internal static class TrayMenuNativeMethods
{
    // ── CalculatePopupWindowPosition ──────────────────────────────────────────
    //
    // API user32 qui calcule la position canonique d'un popup étant donné un
    // point d'ancrage, une taille de fenêtre, des flags d'alignement, et un
    // rect d'exclusion. C'est exactement le calcul que TrackPopupMenu fait en
    // interne — exposé séparément pour les implémentations de popup custom.
    // Tient compte de la taskbar et des limites du moniteur quand TPM_WORKAREA
    // est passé. Retourne la position via popupWindowPosition (pas via la
    // valeur de retour, qui est juste BOOL succès/échec).
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CalculatePopupWindowPosition(
        ref POINT anchorPoint,
        ref SIZE windowSize,
        uint flags,
        ref NativeMethods.RECT excludeRect,
        ref NativeMethods.RECT popupWindowPosition);

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    // ── Flags TPM_* additionnels ──────────────────────────────────────────────
    //
    // TPM_BOTTOMALIGN / TPM_RIGHTALIGN vivent déjà dans Deckle.Core.Interop.
    // TPM_WORKAREA contraint le popup à la zone de travail du moniteur courant
    // (exclut la taskbar). Indispensable pour qu'un menu tray ne déborde pas
    // sous la barre des tâches.

    public const uint TPM_WORKAREA = 0x10000;
    public const uint TPM_VERTICAL = 0x0040;

    // ── Styles fenêtre additionnels ───────────────────────────────────────────
    //
    // WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU. Appliqué post-Loaded
    // au handle du host pour effacer toute trace de caption WinUI 3 héritée
    // d'OverlappedPresenter et présenter un HWND purement popup côté DWM (pas
    // de bordure system, pas de menu système, pas de titlebar).

    public const uint WS_POPUP       = 0x80000000;
    public const uint WS_BORDER      = 0x00800000;
    public const uint WS_SYSMENU     = 0x00080000;
    public const uint WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU;

    // ── ShowWindow nCmdShow additionnels ──────────────────────────────────────
    //
    // SW_HIDE et SW_SHOWNOACTIVATE vivent déjà dans Deckle.Core.Interop.
    // SW_SHOWNORMAL active la fenêtre — nécessaire pour qu'elle reçoive le
    // focus que SetForegroundWindow va ensuite confirmer. Sans activation,
    // le MenuFlyout ne dismiss pas correctement au click-outside.

    public const int SW_SHOWNORMAL = 1;

    // ── DPI per-monitor ───────────────────────────────────────────────────────
    //
    // Le scale appliqué au flyout doit refléter le DPI du moniteur sous le
    // curseur, pas celui où la fenêtre porteuse vit (elle est cachée au boot
    // sur le moniteur primaire). `XamlRoot.RasterizationScale` du frame
    // retourne le scale de ce moniteur primaire, donc faux en multi-monitor
    // ou si l'écran primaire n'est pas à 100 %. On résout proprement avec
    // MonitorFromPoint(curseur) + GetDpiForMonitor.

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const int MDT_EFFECTIVE_DPI = 0;
}
