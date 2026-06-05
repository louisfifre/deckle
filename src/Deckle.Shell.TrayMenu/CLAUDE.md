---
description: WinUI 3 tray context menu — the carrier-window pattern and the DWM pitfalls it hides.
type: agent-instructions
---

# CLAUDE.md — Deckle.Shell.TrayMenu

Renders the tray context menu. Lives as a sibling of [Deckle.Shell](../Deckle.Shell/CLAUDE.md) to keep the shell's Win32-pure invariant intact: Shell carries only system primitives, the menu's WinUI layer is isolated here. `Deckle.App` wires the junction in `OnLaunched`.

## Why a carrier window

A `MenuFlyout` can't anchor to a message-only HWND or a Shell_NotifyIcon icon — it needs a `XamlRoot`, hence a WinUI `Window`. So a transparent `Window` (the "carrier") hosts the flyout, gets positioned on right-click via `CalculatePopupWindowPosition`, and stays invisible while WinUI's internal popup paints the menu. [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) (MIT) solves the same problem; considered as a dependency, rejected — too broad, we own the pattern instead (rationale in the [JOURNAL](./JOURNAL.md)).

## DWM / WinUI pitfalls — the ones that cost a session

- **Carrier invisibility**: `WS_EX_LAYERED` + `SetLayeredWindowAttributes(alpha=0)`, *not* `WS_EX_TRANSPARENT` — the window must keep focus for click-outside dismiss (`Activated → Deactivated`).
- **Residual `WS_CAPTION`**: `SetBorderAndTitleBar(false,false)` isn't enough; a caption residue breaks DWM corners/border. Rewrite `GWL_STYLE` to `WS_POPUPWINDOW` + `SetWindowPos(SWP_FRAMECHANGED)` in `Frame.Loaded` (needs the HWND fully initialized).
- **Anchor from the icon rect** (`Shell_NotifyIconGetRect`), never the cursor — the popup lands tangent to the icon, correct for any taskbar orientation. Cursor + 36×36 exclude is the fallback when the icon can't be located.
- **Prime once**: the first `MenuFlyoutItem` measure is unstable ([microsoft-ui-xaml#7374](https://github.com/microsoft/microsoft-ui-xaml/issues/7374)); prime the visual tree with an invisible `ShowAt+Hide` exactly once in `Frame.Loaded`. Re-priming on each `Show()` detaches the items and collapses `DesiredSize` to zero.
- **`FlyoutPlacementMode.Full`** neutralizes the default placement offset, but stretches the presenter to fill the carrier — so the carrier's measured size dictates the visible size (over-measure → mica gap, under-measure → scroll). Measure on the real `MenuFlyoutPresenter`'s `DesiredSize`.
- **DPI from `GetDpiForMonitor(MonitorFromPoint(cursor))`**, not `XamlRoot.RasterizationScale` — the latter reports the monitor where the carrier is hidden, not where the tray was clicked. Diverges on mixed-scale multi-monitor.
- **Animations off** (`AreOpenCloseAnimationsEnabled = false`): otherwise hiding the carrier mid-close cuts the animation and forces a re-open hack.

## Custom item template must reproduce `PaddingSizeStates`

Any `MenuFlyoutItem` with a custom `ControlTemplate` must reproduce the native `DefaultMenuFlyoutItemStyle`'s `VisualStateGroup x:Name="PaddingSizeStates"` verbatim (`DefaultPadding` ≈ 40 DIP / `NarrowPadding` ≈ 32 DIP), using `ThemeResource` (not `StaticResource`) so the Win11 resource resolves at runtime. The framework switches this state automatically on pointer interaction; a template that omits it stays frozen on its padding while native neighbors go narrow — visual imbalance.

## Ambient Light: custom pill, by choice

The Ambient item uses a custom template with a hand-drawn on/off pill, not the native `ToggleMenuFlyoutItem`. The native checkmark sits in a reserved left icon column and is invisible when unchecked — no affordance that a toggle is even there. This menu is the app's hub; its state must read at a glance. Deliberate departure from "native primitive first"; the back-and-forth (grafted native ToggleSwitch, dimensions, redesign track) is in the [JOURNAL](./JOURNAL.md).

## Pointers

[Deckle.Shell](../Deckle.Shell/CLAUDE.md) owns the Win32 tray and the `RightClickRequested` event; [Deckle.App](../Deckle.App/CLAUDE.md) the wiring sequence and the transverse WinUI pitfalls (UI-thread affinity, `AllowUnsafeBlocks`).
