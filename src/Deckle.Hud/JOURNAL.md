---
description: Diagnosis notes, render doctrine, and deferred work for Deckle.Hud — read on demand, not on every visit.
type: module-journal
---

# JOURNAL — Deckle.Hud

Not read by default. Come here for the *why* behind a HUD choice the code no longer shows — DWM/layered constraints, deferred work, render rules.

## 2026-06-05 — DWM / layered pitfalls of the overlay

Consigned during the doc cleanup; verified against sources where noted.

- **`WS_EX_LAYERED` and the lost shell shadow.** A borderless layered window doesn't get DWM's rich system drop shadow (`CS_DROPSHADOW` has no effect on it) — you draw your own. Verified true for our case; a layered window that kept a frame + `DwmExtendFrameIntoClientArea` could recover it, but that isn't our config. This is *why* the HUD draws its own composite shadow in Composition — and along the way gains the ability to *color* it, which the system shell never would.
- **`WM_NCACTIVATE` forced to `wParam = TRUE`.** Used to keep the HUD painted "active." The message does control active/inactive non-client rendering, and an active window does get a more pronounced DWM shadow — but forcing the wParam alone is not a guaranteed lever on the shadow (it depends on the window's DWM config). Treat it as an observed improvement, not a certainty.
- **Chrono cadence.** The chrono animates on `CompositionTarget.Rendering` (vsync), not a `DispatcherTimer`, to avoid jitter.

## 2026-06-05 — Composite-shadow rule (candidate: belongs to Deckle.Composition / deckle-interface)

Every HUD shadow is a composite of at least two `DropShadow` layers: a **halo/ground** layer (near-zero Y offset, large blur) that diffuses ambient color, and a **drop/fall** layer (large Y offset, high blur) that reads as height. A single layer renders flat — depth perception collapses. This is render doctrine more than HUD doctrine; it should probably live in `Deckle.Composition` or `deckle-interface`. Kept here until that pass.

## 2026-06-05 — Mouse-proximity fade design

Layered alpha mapped to cursor distance through a smoothstep curve, driven by Raw Input (`WM_INPUT`, ~125 Hz) — no polling, no animation timer; fluidity comes from the input frequency. Suspended during a message so it stays fully readable. The high-frequency loop is summarized as one per-session rollup, never one event per tick; a 1 s periodic variant preceded it and was dropped because it flooded the LogWindow with empty events on idle sessions.

## 2026-06-05 — Boot composition warm removed (`PrimeAndHide`)

The former invisible `Charging → Hidden` warm pass paid the first DComp / Bitcount / DWM visual-tree cost at startup — but it added a second synthetic HUD show in the post-build launch path and made z-order diagnosis ambiguous. Removed. The HUD now shows only when a real app state needs it; the first real visible transition may pay the composition cost, which is intentional. The *model* warmup stays on demand in `Deckle.Transcription`. Don't reintroduce a hidden/off-screen priming cycle without a measured first-frame regression and a bounded repro covering the post-build topmost path.

## 2026-06-05 — HDR highlights deferred to V2

Sparkling `> 1.0` scRGB highlights on the chrono stroke aren't achievable on the current stack: `Microsoft.UI.Composition` (Windows App SDK 1.8) has no native HDR / scRGB FP16 swap chain, and its brushes clip to `[0, 1]` sRGB (microsoft-ui-xaml [#777](https://github.com/microsoft/microsoft-ui-xaml/issues/777), [#67](https://github.com/microsoft/microsoft-ui-xaml/issues/67), no roadmap). The only documented workaround is a `D3D11 SwapChainPanel` with a manually allocated HDR10 / scRGB FP16 swap chain and direct D3D rendering — at the cost of giving up declarative Composition animations on the surface. Disproportionate for a 320×64 overlay. Re-eval if: Windows App SDK adds native HDR backdrop support or an extended-range Composition brush, or another Deckle component already needs a custom swap chain (Ambient HDR viewport, calibration tool) and the pooling becomes worthwhile.

## Palette

HUD semantic colors use raw hex, not theme resources: the Attenuated decay (after the peak) needs a specific intermediate color that no single theme resource provides. Only Critical Full matches a system resource exactly (`SystemFillColorCriticalBrush`).
