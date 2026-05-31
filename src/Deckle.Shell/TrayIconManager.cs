using System.Runtime.InteropServices;
using Deckle.Core.Interop;
using Deckle.Catalog;

namespace Deckle.Shell;

// ─── Icône dans la zone de notification système ───────────────────────────────
//
// Implémentation via Shell_NotifyIcon (Shell32) — pas de dépendance WinForms.
//
// Flux des événements :
//   Shell32 → WM_TRAY (WM_USER+1) envoyé au HWND principal
//   SubclassCallback intercepte WM_TRAY
//   → clic gauche → OnToggleRecording invoqué directement
//   → clic droit  → RightClickRequested raised, l'abonné rend le menu
//
// La responsabilité du menu contextuel vit dans le module sibling
// Deckle.Shell.TrayMenu (TrayContextMenuHost) qui s'abonne à
// RightClickRequested et présente un MenuFlyout WinUI 3 natif. Ce module
// reste Win32-pur : il ne connaît pas le contenu du menu.

public sealed class TrayIconManager : IDisposable
{
    private IntPtr _hwnd;
    private IntPtr _hIconIdle;
    private IntPtr _hIconRecording;
    private bool   _iconsOwned;   // false si fallback LoadIcon (icône partagée — pas de DestroyIcon)
    private bool   _iconAdded;
    private bool   _disposed;

    // Délégué SubclassProc — doit vivre dans un champ pour éviter la collecte GC
    private NativeMethods.SubclassProc? _subclassDelegate;
    private static readonly UIntPtr SubclassId = new(0x5752_4159); // "WRAY"

    /// <summary>
    /// Invoked on left-click of the tray icon. Marshaling to the UI thread is
    /// the abonné's responsibility — this handler runs on the message pump
    /// thread of the host HWND.
    /// </summary>
    public Action? OnToggleRecording { get; set; }

    /// <summary>
    /// Raised on right-click of the tray icon. The subscriber renders the
    /// context menu (typically TrayContextMenuHost from Deckle.Shell.TrayMenu).
    /// No payload : the menu host reads the current cursor position itself
    /// via GetCursorPos, since the cursor may move between WM_RBUTTONUP and
    /// the actual menu display.
    /// </summary>
    public event Action? RightClickRequested;

    // ── Initialisation ────────────────────────────────────────────────────────

    public void Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        (_hIconIdle,      _iconsOwned) = LoadIconFromFile(active: false);
        (_hIconRecording, _)           = LoadIconFromFile(active: true);

        // Add the icon in the notification area.
        // Neutral placeholder: UpdateStatus("Ready") from App.OnLaunched
        // replaces this moments later. Keeping the string aligned with
        // UpdateStatus avoids flashing a stale "loading" message since the
        // model is lazy-loaded on first hotkey, not at boot.
        var data = BuildNotifyIconData(Loc.Format("Tray_Tooltip_Format", Loc.Get("Status_Ready")), _hIconIdle);
        bool ok = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!ok)
            throw new InvalidOperationException(
                $"Shell_NotifyIcon(NIM_ADD) échoué — hWnd={_hwnd}, hIcon={_hIconIdle}");
        _iconAdded = true;

        // Subclasser le HWND pour intercepter WM_TRAY
        _subclassDelegate = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero);
    }

    // ── Position rect ─────────────────────────────────────────────────────────
    //
    // Retourne le rect en pixels physiques (screen coordinates) de l'icône
    // dans la zone de notification, ou null si l'icône n'a pas pu être
    // localisée (encore non enregistrée, dans l'overflow caché, ou shell
    // momentanément indisponible pendant un restart d'explorer.exe). Consommé
    // par TrayContextMenuHost pour ancrer son MenuFlyout tangent à l'icône
    // via CalculatePopupWindowPosition — ce qui rend la position
    // automatiquement correcte quelle que soit l'orientation de la taskbar
    // (gauche, droite, bas, haut) et indépendante du point de clic.
    public NativeMethods.RECT? GetIconRect()
    {
        if (!_iconAdded) return null;

        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = 1,
            guidItem = Guid.Empty,
        };

        int hr = NativeMethods.Shell_NotifyIconGetRect(ref id, out NativeMethods.RECT rect);
        return hr >= 0 ? rect : null;
    }

    // ── Status update ───────────────────────────────────────────────────────
    //
    // Wired in App.OnLaunched as the unique sink of TranscriptionEngine.StatusChanged:
    // every transition emitted by the engine (Loading model… → Ready →
    // Recording… → Transcribing… → Rewriting (...)… → Ready) lands here AND
    // in LogService.Status, so the tooltip is by construction in sync with
    // the live pipeline state visible in the LogWindow / app.jsonl.
    //
    // Tip caps at 127 chars (Shell_NotifyIcon szTip limit). Icon swaps to
    // the recording variant whenever the status starts with "Recording" —
    // StartsWith covers both the bare and the ellipsis form ("Recording…").
    public void UpdateStatus(string status)
    {
        bool isRecording = status.StartsWith("Recording");
        IntPtr icon = isRecording ? _hIconRecording : _hIconIdle;

        string tip = Loc.Format("Tray_Tooltip_Format", status);
        if (tip.Length > 127) tip = tip[..127];

        var data = BuildNotifyIconData(tip, icon);
        data.uFlags = NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    // ── Interception des messages tray ────────────────────────────────────────

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_TRAY)
        {
            uint mouseEvent = (uint)(lParam.ToInt64() & 0xFFFF);

            if (mouseEvent == NativeMethods.WM_RBUTTONUP)
            {
                RightClickRequested?.Invoke();
                return IntPtr.Zero;
            }

            if (mouseEvent == NativeMethods.WM_LBUTTONUP)
            {
                // Clic gauche = toggle transcription (équivalent hotkey standard).
                // Permet de lancer/arrêter à la souris quand une seule main est
                // disponible. Logs et Settings passent par le clic droit.
                OnToggleRecording?.Invoke();
                return IntPtr.Zero;
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private NOTIFYICONDATA BuildNotifyIconData(string tip, IntPtr hIcon)
    {
        return new NOTIFYICONDATA
        {
            cbSize          = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd            = _hwnd,
            uID             = 1,
            uFlags          = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAY,
            hIcon           = hIcon,
            szTip           = tip,
            szInfo          = "",
            szInfoTitle     = "",
        };
    }

    // Retourne (hIcon, owned) : owned=true si chargé depuis fichier (→ DestroyIcon requis),
    // owned=false si icône partagée système (→ NE PAS appeler DestroyIcon).
    // Le chemin du .ico vient de IconAssets, source de vérité partagée avec LogWindow.
    private static (IntPtr hIcon, bool owned) LoadIconFromFile(bool active)
    {
        string? path = IconAssets.ResolvePath(recording: active);
        if (path is not null)
        {
            IntPtr hIcon = NativeMethods.LoadImage(
                IntPtr.Zero, path,
                NativeMethods.IMAGE_ICON, 32, 32,
                NativeMethods.LR_LOADFROMFILE);

            if (hIcon != IntPtr.Zero)
                return (hIcon, owned: true);
        }

        // Fallback : icône Windows générique (IDI_APPLICATION = 32512).
        // LoadIcon retourne une icône partagée — NE PAS appeler DestroyIcon dessus.
        // Garantit un item tray visible même si les assets ne sont pas copiés.
        const nint IDI_APPLICATION = 32512;
        return (NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION)), owned: false);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_subclassDelegate is not null && _hwnd != IntPtr.Zero)
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);

        if (_iconAdded)
        {
            var data = BuildNotifyIconData("", IntPtr.Zero);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        }

        // Libérer uniquement les icônes chargées depuis fichier (owned).
        // Les icônes système (LoadIcon) sont partagées — DestroyIcon interdit.
        if (_iconsOwned)
        {
            if (_hIconIdle != IntPtr.Zero)      NativeMethods.DestroyIcon(_hIconIdle);
            if (_hIconRecording != IntPtr.Zero) NativeMethods.DestroyIcon(_hIconRecording);
        }
    }
}
