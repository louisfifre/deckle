---
name: claude-deckle-hud
description: "Doctrine for Deckle.Hud, the HUD window, overlay manager, and composite shadow / proximity-fade mechanics. Read before touching the HUD window, its UserControls, the composite shadows, the hybrid bleed sequence, or the proximity fade."
type: agent-instructions
module: Deckle.Hud
---

# CLAUDE.md — Deckle.Hud

Application HUD module. Covers the main bottom-center window (`HudWindow`, ~320×64, non-resizable `OverlappedPresenter`), the stacked transient cards (`HudOverlayWindow`, `HudOverlayManager`), the stopwatch UserControl with progressive coloring and Composition stroke (`HudChrono`), the visual state enum (`HudState`), and internal UI primitives (`HudMessage`, `HudPalette`, `MessageKind`). Born from the merge of `Deckle.Chrono.Hud` (dissolved) and the extraction of HUD-side code that lived in `Deckle.App` before the mapping cleanup.

## Architecture — technical shell + UserControls

`HudWindow` is a technical shell: it carries the HWND, the extended styles, the proximity subclass, the backdrop, and the state dispatcher. No visual logic of its own — that is split across two UserControls. `HudChrono` (314×78) carries the Bitcount chronometer + status dot + stroke and shadow surface for Charging / Recording / Transcribing / Rewriting. `HudMessage` (272×78 card inside a 400×160 window) carries title + subtitle + semantic badge for Pasted / Copied / Error / UserFeedback; the card is centered in the window so the composite shadow can overflow into the transparent margin. The XAML of `HudWindow` boils down to a `Grid` that stacks `HudChrono` (Visible by default) and `HudMessage` (Collapsed by default). No `Background` on the Grid: each UserControl carries its own `CardBackgroundFillColorDefaultBrush`, the window itself stays transparent (acrylic + content alpha-blended).

## State machine

Single enum `HudState { Hidden, Charging, Recording, Transcribing, Rewriting, Message }`. Plus `MessageKind { Success, Critical, Warning, Informational }` and `MessagePayload(Kind, Title, Subtitle, Duration)`. All transitions go through `HudWindow.SetState(next, msg = null)` — a single source of truth. Every public call (`ShowPreparing`, `ShowRecording`, `SwitchToTranscribing`, `SwitchToRewriting`, `ShowError`, `ShowPasted`, `ShowCopied`, `ShowUserFeedback`, `Hide`) marshals into `SetState`. The dispatcher cancels in-flight timers (message auto-hide + retract, fade-in), toggles `Chrono.Visibility` / `Message.Visibility`, forwards to the relevant control (`Chrono.ApplyState(state)` or `Message.Show(payload)`), recomputes size and position through `ShowNoActivate` (DPI-aware), applies alpha through `ApplyShowAlpha` (warm pass force, 150 ms fade-in if `Hidden→visible` and `Overlay.Animations` on, instant alpha otherwise), activates proximity at the end of the transition, and for messages arms `_messageHideTimer` (full duration) plus `_messageRetractTimer` (~800 ms if duration > 1 s).

`HideSync` is the blocking variant via `ManualResetEventSlim` rendezvous, called right before `PasteFromClipboard` to guarantee that `SW_HIDE` is effective before `SendInput` sends the Ctrl+V — without that lock, activation redistribution can hijack the paste.

## Semantic palette

Centralized in `HudPalette.cs`. Each kind has a Full color (at the peak), an Attenuated color (after decay), and a Segoe Fluent glyph carried by the 16×16 badge. Success Full `#0F7B0F` / Attenuated `#345634` / glyph ``. Critical Full `#C42B1C` / Attenuated `#8C5954` / glyph ``. Warning Full `#9D5D00` / Attenuated `#62523B` / glyph ``. Informational Full `#005FB7` / Attenuated `#455C72` / glyph ``. The Critical Full hex matches `SystemFillColorCriticalBrush` Win11 exactly; the other mappings do not align with a single theme resource because the Attenuated decay requires a specific intermediate color — hence the raw hex values.

## Composite shadows — cardinal rule

**All HUD shadows are composite multi-layer shadows**. A single `DropShadow` produces a flat render; the Win11 render always comes from stacking at least two layers that play distinct roles. The **halo / ground layer** has a small offset (Y close to 0) and a large blur — it diffuses the ambient color around the surface. The **drop / fall layer** has a large downward Y offset and high blur — it creates the perception of height, the "great recess" from the background. Both together produce the read. If only one is laid down, depth perception collapses.

The rule applies to the three HUD composite shadows. Transcribing uses 2 halo + drop layers in gray. Rewriting uses 8 colored layers (1 per arc, distributed at 45°). Message uses 2 halo + drop layers in semantic color. No layer can be omitted — the rule extends to any future surface that would have to carry a shadow.

## Composition pipeline

`HudComposition.cs` (static helper, pure `Microsoft.UI.Composition`, zero Win2D) hosts the three shadow constructors. **Silhouette pattern**: a `DropShadow` takes the silhouette of the Visual it decorates. To obtain a shadow conformed to a rounded rect without Win2D, we host a `ShapeVisual` filled with a near-invisible brush (alpha `0x01`, i.e. 1/255) inside a `LayerVisual`. The layer rasterizes the fill, the shadow reads the silhouette. The fill is invisible to the eye but enough to drive the shape.

`CreateTranscribingStroke(compositor, size)` returns a `ContainerVisual` with a drop layer `offset=(-12,12)` / `blur=64` / `color=#66666647`, a halo layer `offset=(-2,2)` / `blur=21` / `color=#66666638`, and a diagonal stroke gradient (`(0,0)→(1,1)`) from `#757575` to `#BFBFBF`, thickness 1, geometry `RoundedRectangleGeometry` corner radius 8.

`CreateRewritingStroke(compositor, size)` returns a `ContainerVisual` with 8 colored shadows at radius 32, alpha `0x40`, distributed at the 8 angles (0, 45°, 90°…) with red, amber, lime, green, cyan, blue, violet, magenta colors. 8 trim arcs around the perimeter of the rounded rect — each with its own `RoundedRectangleGeometry` because `TrimStart`/`TrimEnd` is a property of the geometry, not of the shape (non-shareable geometries). Epsilon `0.005` added to each `TrimEnd` to mask the seam between adjacent arcs.

`CreateMessageShadow(compositor, size, full, attenuated)` returns `(ContainerVisual host, CompositionPropertySet anim)`. The `PropertySet` exposes a scalar `Saturation` (1.0 = full, 0.0 = attenuated). Both layers share the same lerped color through an `ExpressionAnimation` `ColorLerp(props.AttenuatedColor, props.FullColor, props.Saturation)`. The animation runs at the Composition level, **off UI thread**. Drop layer `offset=(0,32)` / `blur=64`. Halo layer `offset=(0,2)` / `blur=21`. `AnimateShadowToAttenuated(compositor, props, duration)` is a `ScalarKeyFrameAnimation` on `props.Saturation` from 1.0 to 0.0, easing `CubicBezier((0.05, 0.95), (0.2, 1.0))` (peak then plateau, mimics the requested log-decay). Default duration 650 ms called by `HudMessage.AnimateToAttenuated`.

## Hybrid bleed — 400×160 then retract to 272×78

The goal of the hybrid resize: let the composite shadow overflow far around the card at the peak, then retract the window to the card size once the shadow has attenuated. Sequence for a message: at `t=0`, `SetState(Message)` resizes the window to `HUD_WIDTH_MESSAGE × HUD_HEIGHT_MESSAGE` (400×160); the 272×78 card is centered in the grid (`HorizontalAlignment="Center" VerticalAlignment="Center"`), transparent margin ~64 px horizontal ~41 px vertical, the composite shadow fills that margin. Still at `t=0`, `Message.Show(payload)` creates the composite shadow (full) and starts the `Saturation` animation (650 ms toward attenuated). At `t=650 ms`, animation done, shadow in attenuated color. At `t=800 ms`, `_messageRetractTimer` ticks: `_messageRetracted=true`, `ShowNoActivate()` re-called, the window shifts to `HUD_WIDTH_MESSAGE_RETRACTED × HUD_HEIGHT_MESSAGE_RETRACTED` (272×78). The window snaps to the exact size of the card; the attenuated shadow keeps existing but is clipped by the window edge — visually the halo "settles" while the card returns to its standard size. At `t=duration` (e.g. 5 s for Critical), `_messageHideTimer` ticks, `SetState(Hidden)`.

For short messages (Pasted at 500 ms), the retract is skipped (`MessageRetractMinDuration = 1 s`) — the window disappears before there is time to retract. `HudMessage` re-centers its shadow visual on every `OuterRoot.SizeChanged`: the offset baked at `Show` time (computed for 400×160) would become incorrect after the retract. The subscription decouples the shadow from the window resize. `ShowNoActivate` picks the size based on `(_state, _messageRetracted)`: `Message && !retracted → 400×160`; `Message && retracted → 272×78`; otherwise 314×78 (chrono). DPI-aware: the DIP value is multiplied by `GetDpiForWindow(_hwnd) / 96.0` on every show, so a runtime DPI change between two dictations is absorbed without restarting the app.

## Progressive coloring of digits (HudChrono)

Each chrono digit that changes at least once switches to red `SystemFillColorCriticalBrush` (Windows theme resource, follows light/dark) and stays there until the next `ApplyState(Recording)`. The initial `0` stays neutral as long as it has not moved. Idle Runs do not carry a local Foreground — they inherit from `ClockText.Foreground = {ThemeResource TextFillColorPrimaryBrush}`, auto-reactive to the theme. The reset is `ClearValue(TextElement.ForegroundProperty)` on each named Run.

`ChronoRoot.ActualThemeChanged` re-resolves the critical brush and re-applies it to digits already lit (a Foreground assigned in code does not follow a `ThemeResource` binding, it has to be reassigned manually). Cadence: `CompositionTarget.Rendering` (vsync, no `DispatcherTimer` → no jitter). Hooked only in Recording, unhooked in Transcribing/Rewriting (the chrono freezes on the last value).

## Stroke surface (HudChrono)

`ProcessingSurfaceHost` is a transparent `Border` `IsHitTestVisible=False` that covers both chrono columns. It is the attach point for `ElementCompositionPreview.SetElementChildVisual` for the Composition visuals returned by `HudComposition.CreateTranscribingStroke` / `CreateRewritingStroke`. `AttachProcessingVisual` falls back to dims (314, 78) if `ActualWidth/Height = 0` at first attach (before layout pass). The visual is not auto-resized on subsequent layout — but Charging/Recording always reset the surface, and the user only reaches Transcribing after at least one chrono measurement.

## Mouse proximity fade — event-driven

Through Raw Input (`RegisterRawInputDevices` + `RIDEV_INPUTSINK`) intercepted by HWND subclass (`SubclassProc` as instance field to escape GC, ID `0x48554450`). Global layered alpha (`WS_EX_LAYERED` + `SetLayeredWindowAttributes(LWA_ALPHA)`) covering Acrylic + content, mapped to cursor distance through smoothstep. `NEAR_RADIUS_DIP=10` → MIN alpha (40, faded), `FAR_RADIUS_DIP=128` → MAX alpha (255, full), between the two a `t²(3-2t)` curve smooth without break at the edges. No polling, no animation timer — fluidity comes from the frequency of `WM_INPUT` (~125 Hz). Toggleable through `Settings.Overlay.FadeOnProximity`.

Subclass `WM_NCACTIVATE`: forces `wParam=TRUE` permanently so DWM paints the HUD as active (rich "Active Window" drop shadow instead of the flattened inactive shadow). Extended styles set in the constructor: `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT`. Message state note: `_proximityActive = false` during a message, the fade is suspended (the message must stay fully readable even if the cursor is over it).

## Window spec

Non-resizable `OverlappedPresenter`, `IsAlwaysOnTop=true`, `ExtendsContentIntoTitleBar=true`, `hasTitleBar: false`. Position horizontally centered, vertical anchor configurable through `Settings.Overlay.Position` (TopCenter or BottomCenter, default BottomCenter). Legacy corner values (TopLeft / BottomRight / …) possibly present in an old `settings.json` are normalized to TopCenter / BottomCenter by `StartsWith("Top")`.

Show goes through `MoveAndResize` (recomputes DPI on each show) then `ShowWindow(SW_SHOWNOACTIVATE)` + `SetWindowPos(HWND_TOP, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE)`. **Never `SetForegroundWindow`** — the HUD must not steal focus. Hide is `ShowWindow(SW_HIDE)`. The window is created once in `OnLaunched`, never destroyed (`Closing → Cancel`). UI handlers are marshaled through `DispatcherQueue.TryEnqueue` because `TranscriptionEngine` events come from background threads. The overlay is toggleable in Settings (`Overlay.Enabled`), checked at the top of `SetState`.

**Backdrop**: `DesktopAcrylicBackdrop` (canonical material for Win11 transient windows). DWM signal `DWMSBT_TRANSIENTWINDOW` explicitly set (correct intent per doc, even if the rich Shell shadow of system menus is not accessible to unpackaged WinUI 3 — validated at runtime 2026-04-09).

## Warm boot — invisible through layered alpha

`PrimeAndHide()` is called once in `App.OnLaunched` after the creation of the `HudWindow`. The goal: pay the cost of the first composition (DComp swap chain, Bitcount font shaping, DWM visual tree) at boot rather than on the first hotkey, so the real first show is cold-path-free. The shape: `SetState(HudState.Charging, bypassGate: true, alphaOverride: 0)` presents the window, the compositor runs, then `SetAlphaImmediate(0)` makes the layered layer transparent — nothing reaches the screen. A Low priority dispatch chains `SetState(HudState.Hidden)` → `SW_HIDE` + `SetAlphaImmediate(MAX_ALPHA)` which resets alpha for the next real show.

Effect: no visible flash at boot, no risk of a partial first frame (DWM outline without content) that could have signaled a broken startup. The warm does its job silently. The warm pass activates `bypassGate: true` (a disabled `Overlay.Enabled` Settings must not short-circuit the warm) and `alphaOverride.HasValue` prevents `ApplyShowAlpha` from activating proximity (a cursor present in the HUD zone at boot could otherwise overwrite alpha=0 through smoothstep). No off-screen relocation — the warm pays the composition cost exactly at the real position (DPI, work area, Settings anchor).

## Real show — 150 ms fade-in

Any `Hidden → visible` (Charging, Recording, Transcribing, Rewriting, Message) triggers a 150 ms cubic ease-out fade-in (`1 - (1-t)^3`), aligned with `WindowSlideAnimator` / `LayeredAlphaAnimator` of the overlay subsystem. The transition is centralized in `ApplyShowAlpha`. If `alphaOverride.HasValue`, it is a warm pass, alpha forced, proximity skipped. Otherwise, if `!wasShown && AnimationSystemSetting.AreClientAreaAnimationsEnabled()`, `StartFadeIn(MAX_ALPHA)`; during the fade `_proximityActive = false`, at the end proximity is re-activated + immediate call to `UpdateProximity`. Otherwise (state switch in already-visible, or animations off), `SetAlphaImmediate(MAX_ALPHA)` instant + immediate proximity.

The implementation is inline (dedicated `_fadeInTimer` at ~60 fps) rather than through `LayeredAlphaAnimator` to keep `_currentAlpha` as the single source of truth on the `HudWindow` side — the shared instance avoids desyncs with the proximity fade. Gating through `Settings.Overlay.Animations` (and not `SPI_GETCLIENTAREAANIMATION`) for the same reason as the rest of the HUD subsystem: HUD transitions are load-bearing for tracking which state just replaced which other, we do not automatically piggyback on the Windows global reduce-motion pref.

## Layered constraint and Shell shadow

`WS_EX_LAYERED` disables by design the rich system DWM Shell shadow. This is a basic Win32 constraint, no DWM API works around it for an unpackaged WinUI 3. The "shell shadows" of Explorer context menus go through a private DWM path that is inaccessible. This is precisely the observation that validates the Composition approach: we cannot have the Shell shadow, so we draw our own — and along the way we gain the possibility to **color** it (which the system shell would not do). For the Transcribing / Rewriting / Message states, the composite shadow is the right tool. For the Charging / Recording states (no Composition overlay), we keep `WM_NCACTIVATE → TRUE` forced to slightly improve the default DWM render.

## Engine wiring

`App.xaml.cs` dispatches `TranscriptionEngine.StatusChanged` to the public methods of `HudWindow`: `"Recording" → ShowRecording()`, `"Transcribing" → SwitchToTranscribing()`, `"Réécriture"` or `"Rewriting"` (prefix) `→ SwitchToRewriting()`. The dual FR/EN prefix is defensive as long as the sweep has not fully landed on EN — the dispatcher stays robust on both strings in the meantime.

## HDR highlights — deferred to V2

The idea of sparkling `> 1.0` scRGB highlights on the chrono conic stroke and notification transitions through the display HDR headroom is not achievable on the current stack. `Microsoft.UI.Composition` Windows App SDK 1.8 does not support a native HDR / scRGB FP16 swap chain, and its brushes (`CompositionSolidColorBrush`, `CompositionLinearGradientBrush`, `CompositionSurfaceBrush`) implicitly clip to `[0, 1]` sRGB. Confirmed by [microsoft-ui-xaml#777](https://github.com/microsoft/microsoft-ui-xaml/issues/777) and [microsoft-ui-xaml#67](https://github.com/microsoft/microsoft-ui-xaml/issues/67), with no roadmap.

The only documented workaround is a `D3D11 SwapChainPanel` interoped with an HDR10 or scRGB FP16 swap chain manually allocated and direct D3D rendering — at the price of giving up declarative Composition animations (`Expression`, `ImplicitAnimations`, `Effects`) on the surface. Disproportionate for the perceptual benefit on a secondary 320×64 overlay. Re-eval triggers: Windows App SDK adds native HDR backdrop support on `Window` or an extended-range Composition brush, or another Deckle component already requires a custom swap chain (Ambient HDR debug viewport, calibration tool) and the pooling becomes worthwhile.

## Observability

All emissions go through `DeckleHudSource.Log` — `Deckle.Hud` provider exposed as a static singleton, HUD tag in the LogWindow.
