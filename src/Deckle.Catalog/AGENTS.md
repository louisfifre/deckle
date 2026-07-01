---
description: Shared WinUI floor — the UI primitives every module depends on and that depend on none: localized strings, glyphs, the declarative settings model, the confirmation gate.
type: agent-instructions
---

# AGENTS.md — Deckle.Catalog

The shared WinUI floor — the UI primitives every module depends on and that themselves depend on no module. Two are resources: **localized strings** (the `Loc` facade over the Windows App SDK `ResourceLoader`, used in code via `Loc.Get` / `Loc.Format` and in XAML via `x:Uid`; each module ships its own `Strings/<lang>/Resources.resw`, English-first) and **Segoe Fluent glyphs** (semantic keys mirrored in `Themes/Icons.xaml` for XAML and `Glyphs.cs` for code — change one, change both). The rest are cross-cutting UI mechanisms that share that floor-position: the **declarative settings model** (`SettingsComposer` + `SettingDescriptor`) and the **confirmation gate** (`ConfirmationService`).

Naming: reuse a `Common_*` key before inventing a surface-specific one; technical identifiers (file names, endpoints, brand and model names, EventSource providers) stay hardcoded, never localized.

## The four that bite

- **Code-style keys (`Loc.Get`) resolve from the root resource map only** — Deckle.App's `Resources.resw`. A library's own `.resw` lands in its own PRI subtree, reachable by `x:Uid` but invisible to `Loc`; unpackaged MRT then *throws* (`NamedResource Not Found`) rather than returning empty. Mirror every module code-behind key into Deckle.App's `.resw`; the module copy stays the source of truth for wording.
- **Use the Windows App SDK `ResourceLoader`** (`Microsoft.Windows.ApplicationModel.Resources`), never the legacy UWP one — only it works unpackaged.
- **`<DefaultLanguage>en-US</DefaultLanguage>` in every csproj carrying `.resw`** — without it there's no declared fallback and `x:Uid` values come up empty when the system language diverges.
- **Never share one `x:Uid` across different element types** — MRT applies every property declared in the `.resw` to each element under that Uid, so a `Button` (which has `.Content`, not `.Text`) under a Uid that declares `.Text` crashes the whole page load at runtime. One Uid per element type; same-type elements may share.
