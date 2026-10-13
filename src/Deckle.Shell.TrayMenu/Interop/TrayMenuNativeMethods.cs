namespace Deckle.Shell.TrayMenu;

// ─── Tray menu specific Win32 constants ───────────────────────────────────────
//
// Generic imports (GetCursorPos, SetForegroundWindow, ShowWindow,
// SetWindowLongPtr, SetLayeredWindowAttributes, SetWindowSubclass…) live in
// Deckle.Core.NativeMethods and are consumed as-is. Here we only add what is
// missing: the anchor window's popup style and its activating show command.

internal static class TrayMenuNativeMethods
{
    // ── Window styles ─────────────────────────────────────────────────────────
    //
    // WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU. Applied post-Loaded
    // to the anchor HWND to clear any WinUI 3 caption trace inherited from
    // OverlappedPresenter and present a pure popup HWND to the system (no
    // caption, so no system minimum size either).

    public const uint WS_POPUP       = 0x80000000;
    public const uint WS_BORDER      = 0x00800000;
    public const uint WS_SYSMENU     = 0x00080000;
    public const uint WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU;

    // ── ShowWindow nCmdShow ───────────────────────────────────────────────────
    //
    // SW_HIDE and SW_SHOWNOACTIVATE already live in Deckle.Core. SW_SHOWNORMAL
    // activates the window; required so it receives the focus that
    // SetForegroundWindow then confirms. Without activation, the menu does not
    // dismiss on a click outside.

    public const int SW_SHOWNORMAL = 1;
}
