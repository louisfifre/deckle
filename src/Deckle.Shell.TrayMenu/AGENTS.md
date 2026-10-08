---
description: WinUI 3 tray context menu — carrier-window pattern and its DWM pitfalls.
type: agent-instructions
---

# AGENTS.md — Deckle.Shell.TrayMenu

A `MenuFlyout` needs a `XamlRoot`, so an invisible WinUI `Window`, the carrier, hosts it. H.NotifyIcon was rejected as too broad: see the [JOURNAL](./JOURNAL.md). The module lives beside Deckle.Shell so the shell stays XAML-free.

## Pitfalls

- Hide the carrier with `WS_EX_LAYERED` and `SetLayeredWindowAttributes(alpha=0)`, not `WS_EX_TRANSPARENT`: it must keep focus so a click outside dismisses.
- A `WS_CAPTION` residue breaks DWM corners. In `Frame.Loaded`, rewrite `GWL_STYLE` to `WS_POPUPWINDOW` with `SetWindowPos(SWP_FRAMECHANGED)`.
- Anchor from `Shell_NotifyIconGetRect`, never the cursor, so any taskbar orientation works. The cursor is only the fallback.
- Prime once in `Frame.Loaded` with an invisible `ShowAt` and `Hide`: the first measure is unstable ([microsoft-ui-xaml#7374](https://github.com/microsoft/microsoft-ui-xaml/issues/7374)). Priming on each `Show()` collapses `DesiredSize` to zero.
- `FlyoutPlacementMode.Full` stretches the presenter to the carrier. Size the carrier from the real `MenuFlyoutPresenter`'s `DesiredSize`.
- Take the DPI from `GetDpiForMonitor(MonitorFromPoint(cursor))`. `XamlRoot.RasterizationScale` reports the carrier's monitor.
- Set `AreOpenCloseAnimationsEnabled = false`, or hiding the carrier cuts the close animation.

## Custom items

- A custom `MenuFlyoutItem` template reproduces the native `PaddingSizeStates` group verbatim, with `ThemeResource` so the Windows 11 resource resolves at runtime. Without it the item ignores narrow padding.
- The Ambient item's hand-drawn pill is a deliberate departure from native-first: `ToggleMenuFlyoutItem` shows nothing when unchecked. See the [JOURNAL](./JOURNAL.md).

## Pointers

[Deckle.Shell](../Deckle.Shell/AGENTS.md) owns the Win32 tray and `RightClickRequested`. [Deckle.App](../Deckle.App/AGENTS.md) owns the wiring order and the shared WinUI 3 pitfalls.
