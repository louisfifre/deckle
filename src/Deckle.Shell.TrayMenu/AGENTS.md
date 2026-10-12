---
description: WinUI 3 tray surfaces — the right-click context menu and the left-click quick-actions panel, their carrier-window pattern and its DWM pitfalls.
type: agent-instructions
---

# AGENTS.md — Deckle.Shell.TrayMenu

A flyout needs a `XamlRoot`, so an invisible WinUI `Window`, the carrier (`TrayCarrierWindow`), hosts it. Two hosts use it, each with its own carrier instance: `TrayContextMenuHost` (right click, a `MenuFlyout`) and `TrayQuickActionsHost` (left click, a `Flyout` of real controls). H.NotifyIcon was rejected as too broad: see the [JOURNAL](./JOURNAL.md). The module lives beside Deckle.Shell so the shell stays XAML-free.

## Pitfalls

- Hide the carrier with `WS_EX_LAYERED` and `SetLayeredWindowAttributes(alpha=0)`, not `WS_EX_TRANSPARENT`: it must keep focus so a click outside dismisses.
- A `WS_CAPTION` residue breaks DWM corners. In `Frame.Loaded`, rewrite `GWL_STYLE` to `WS_POPUPWINDOW` with `SetWindowPos(SWP_FRAMECHANGED)`.
- Anchor from `Shell_NotifyIconGetRect`, never the cursor, so any taskbar orientation works. The cursor is only the fallback.
- Prime once in `Frame.Loaded` with an invisible `ShowAt` and `Hide`: the first measure is unstable ([microsoft-ui-xaml#7374](https://github.com/microsoft/microsoft-ui-xaml/issues/7374)). Priming on each `Show()` collapses `DesiredSize` to zero.
- `FlyoutPlacementMode.Full` stretches the presenter to the carrier. Size the carrier from the real presenter's `DesiredSize` (`MenuFlyoutPresenter` for the menu, `FlyoutPresenter` for the panel), and only when it holds the items, or the panel's content: a degenerate measure reports the presenter's minimum size, one item tall (`TrayMenuSizing`).
- A `Standard` flyout moves keyboard focus into its popup, a top-level window the carrier owns, which can deactivate the carrier. Never hide on a bare deactivation: the carrier raises `Deactivated` only toward a window it does not own (`WM_ACTIVATE` lParam, `TrayWindowOwnership`), and a light dismiss of the flyout arms the same reopen guard as a deactivation.
- Take the DPI from `GetDpiForMonitor(MonitorFromPoint(cursor))`. `XamlRoot.RasterizationScale` reports the carrier's monitor.
- Set `AreOpenCloseAnimationsEnabled = false`, or hiding the carrier cuts the close animation.

## Items

- Menu items are native `MenuFlyoutItem`s with no template override. Their padding is set to the narrow value at build (`ApplyNarrowPadding`), so the first render is already compact.
- Module switches live in the quick-actions panel as native `ToggleSwitch` rows, not in the menu: a `MenuFlyoutItem` has no slot for a switch, and every item click closes the menu. See the [JOURNAL](./JOURNAL.md).

## Pointers

[Deckle.Shell](../Deckle.Shell/AGENTS.md) owns the Win32 tray, `LeftClickRequested`, `DoubleClickRequested` and `RightClickRequested`. [Deckle.App](../Deckle.App/AGENTS.md) owns the wiring order and the shared WinUI 3 pitfalls.
