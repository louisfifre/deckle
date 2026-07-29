---
name: deckle-interface
description: Render the visual interface at Microsoft first-party level — native primitives, theme resources, system materials. Invoke before touching any UI surface.
type: skill
---

# Deckle — Interface

## Intent

Render the visual interface at the level Microsoft would ship — as reflexes, not by reinventing what Windows already gives. The exhaustive WinUI control and theme-resource catalog is generic knowledge: reach for `winui-app` and Microsoft Learn, not a copy here.

## How

Native primitives first. Before drawing anything — even what looks simple — ask which Windows primitive, control, theme resource, or canonical pattern already covers the need. Reinvention tells: a manual border where a Card resource exists, a hand-drawn shadow instead of the DWM shadow, a numeric radius instead of the corner-radius resource, the wrong backdrop for the window's lifetime, custom caption buttons instead of the native TitleBar.

Zero magic values. No hex colour, no numeric corner radius, no arbitrary thickness, no hand-computed shadow — everything goes through theme resources that follow light, dark, contrast and accent on their own. A design value with no theme-resource equivalent signals the wrong primitive — go back to the spec and find the right control.

System-managed materials. Mica on long-lived windows, Acrylic on transients (popups, menus, dialogs, HUD, notifications); DWM owns the shadows and the rounded corners, not XAML.

Linear animations by default. No custom easing unless explicitly asked — the default curve is linear, and curves are introduced deliberately, one validated at a time.

Respect deliberate design choices. An existing visual element — shadow, fade, stroke, padding, radius — is a deliberate asset, not a cost to optimize away. Find a solution that preserves it, never one that drops it because it "brings nothing".

When in doubt, three live references: the WinUI Gallery for a control's canonical behaviour, PowerToys for HUD/tray/autostart patterns, Windows 11 Settings and Explorer for adaptive navigation, settings cards, auto-save and TitleBar.
