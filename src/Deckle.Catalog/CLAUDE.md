---
description: UI resource catalog — localized strings (Loc / x:Uid) and Segoe Fluent glyphs, centralized for every WinUI module.
type: agent-instructions
---

# CLAUDE.md — Deckle.Catalog

The central home for two kinds of UI resource: **localized strings** (the `Loc` facade over the Windows App SDK `ResourceLoader`, used in code via `Loc.Get` / `Loc.Format` and in XAML via `x:Uid`; each module ships its own `Strings/<lang>/Resources.resw`, English-first) and **Segoe Fluent glyphs** (semantic keys mirrored in `Themes/Icons.xaml` for XAML and `Glyphs.cs` for code — change one, change both).

Naming: reuse a `Common_*` key before inventing a surface-specific one; technical identifiers (file names, endpoints, brand and model names, EventSource providers) stay hardcoded, never localized.

## The three that bite

- **Use the Windows App SDK `ResourceLoader`** (`Microsoft.Windows.ApplicationModel.Resources`), never the legacy UWP one — only it works unpackaged.
- **`<DefaultLanguage>en-US</DefaultLanguage>` in every csproj carrying `.resw`** — without it there's no declared fallback and `x:Uid` values come up empty when the system language diverges.
- **Never share one `x:Uid` across different element types** — MRT applies every property declared in the `.resw` to each element under that Uid, so a `Button` (which has `.Content`, not `.Text`) under a Uid that declares `.Text` crashes the whole page load at runtime. One Uid per element type; same-type elements may share.
