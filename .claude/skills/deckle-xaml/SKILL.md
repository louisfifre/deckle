---
name: deckle-xaml
description: Transverse XAML rendering doctrine for Deckle (.NET 10 / WinUI 3): native primitives before custom drawing, zero magic values (theme resources only), system-managed materials and shadows, linear animations by default, respect for deliberate design choices. Invoke before touching any XAML surface. Triggers like deckle xaml, native primitives, zero magic, theme resource, Mica or Acrylic backdrop, corner radius, animations, render a control.
type: skill
---

# Deckle — Transverse XAML doctrine

## Role

Applies to every Deckle XAML surface, on top of each module's own doctrine. It translates the root goal — a rendering at Microsoft first-party level — into reflexes. Invoked before touching any XAML. The exhaustive control and theme-resource catalog is generic WinUI knowledge: reach for `winui-app` and the Microsoft Learn MCP, not a copy here.

## Doctrine

**Native primitives first.** Before drawing anything — including cases that look simple — ask which Windows primitive, control, theme resource, or canonical pattern already covers the need. Reinvention tells: a manual border where a Card theme resource exists, a hand-drawn shadow instead of the DWM Shell shadow, a numeric radius instead of `OverlayCornerRadius` / `ControlCornerRadius`, `MicaBackdrop` on a transient window instead of `DesktopAcrylicBackdrop`, custom caption buttons instead of the native `TitleBar`.

**Zero magic values.** No `#xxxxxx`, no numeric `CornerRadius="7"`, no arbitrary `BorderThickness`, no hand-computed `BoxShadow`. Everything goes through Windows theme resources that follow light, dark, contrast, and accent on their own — `LayerFillColorDefaultBrush`, `CardBackgroundFillColorDefaultBrush`, `CardStrokeColorDefaultBrush`, `OverlayCornerRadius`, `ControlCornerRadius`, `SystemFillColor*`, `TextFillColor*`. A Figma value with no theme-resource equivalent signals the wrong primitive — go back to the spec and find the right control.

**System-managed materials.** `MicaBackdrop` on long-lived app windows, `DesktopAcrylicBackdrop` on transients (popups, menus, dialogs, HUD, notifications). DWM applies the matching rendering, Shell shadow on transients included. Shadows and rounded corners are DWM's responsibility, not XAML's.

**Linear animations by default.** No custom easing without an explicit request from Louis — the default curve is linear. He handles curves in a dedicated pass and validates each cubic-bezier as he introduces it. Assumed exception: the HUD/overlay subsystem (`src/Deckle.Hud/CLAUDE.md`), where cubic-bezier animations already align on the existing animators.

**Respect deliberate design choices.** An existing visual element (shadow, fade, stroke, specific padding, border-radius) is a deliberate asset, not a cost to optimize. Find a solution that preserves it, never one that removes it because it "brings nothing".

When in doubt about a rendering or a pattern, three live references: the WinUI 3 Gallery for a control's canonical behavior, PowerToys for HUD / tray / autostart patterns, Windows 11 Settings and Explorer for adaptive `NavigationView`, `SettingsCard`, auto-save, and `TitleBar`.

## Pointers

- **`winui-app`** / Microsoft Learn MCP — the exhaustive WinUI control, theme-resource, and materials catalog. This skill states Deckle's posture; these carry the generic how-to it points to.
- **`deckle-settings-ux`** — what to expose and how to organize it (information architecture); the complement to this rendering doctrine.
- **`deckle-nomenclature`** — naming of theme resources, including local `<Domain>.<Descriptor>.<Variant>` resources.
