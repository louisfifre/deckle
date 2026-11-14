---
name: deckle-interface
description: Use when you build or change anything a person sees in a Deckle window, from a XAML page to the HUD, to render it the way Microsoft ships it, from native controls, theme resources and system materials.
metadata:
  version: "1.2"
---

## Why

Windows already draws most of what an interface needs, and what it draws follows the theme, the contrast and the accent on its own. Reaching for it first is what keeps Deckle at the level AGENTS.md sets. The catalogue of controls and theme resources is generic knowledge, so it lives in `winui-app` and Microsoft Learn, not here.

## Statement

Before drawing anything, even what looks simple, find the Windows primitive, control, theme resource or canonical pattern that already covers the need. Where a Deckle component already wraps that primitive, use the component. Something drawn by hand that Windows already draws is the sign to look again. When Windows has nothing for the need, look for the component Deckle already has. A need that no existing component covers means stop, the way a fourth level means rework. Ask first whether the need can be reframed on what exists, and say so before building anything new.

Every design value comes from a theme resource, so it follows light, dark, contrast and accent by itself. A value with no theme resource means the primitive is wrong. Go back to the spec and find the right control.

The material follows the surface's lifetime. A window the user works in for a long time wears Mica. A surface that appears briefly over other work wears Acrylic. DWM draws the shadows and rounds the corners, not XAML.

Animations run on a linear curve. Another curve enters only when the maintainer asks for it, and each one is validated on its own. A built-in theme transition keeps the curve Windows gives it. Every animation follows the Windows animation preference and snaps to its end when the person has turned animations off.

The title bar takes the height of what it holds. A bar that carries a control, such as a search box, asks for tall caption buttons, which line up with that control. A bar that carries only the icon and the title keeps the standard height, so the caption buttons stay inside it and the window's content starts right under it.

One `x:Uid` serves one element type. MRT applies every property of the `.resw` entry to each element that carries the Uid, and a property the element does not have crashes the page at load.

An existing visual element was chosen on purpose, whatever its kind. Find a solution that keeps it. When it breaks a rule above, ask the maintainer before replacing it.

When in doubt, check against three live references. The WinUI Gallery shows a control's canonical behaviour. PowerToys shows how a resident utility handles its HUD, tray and autostart. Windows 11 Settings and Explorer show adaptive navigation, settings cards, auto-save and the TitleBar.
