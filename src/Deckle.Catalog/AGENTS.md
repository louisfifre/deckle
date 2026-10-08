---
description: Shared WinUI floor — strings, glyphs, settings model, confirmation gate.
type: agent-instructions
---

# AGENTS.md — Deckle.Catalog

## Strings and glyphs

- Reuse a `Common_*` key before inventing a surface-specific one.
- Technical identifiers (file names, endpoints, brand and model names, EventSource providers) are never localized.
- Glyph keys live in both `Themes/Icons.xaml` and `Glyphs.cs`. Change both.

## The four traps

- `Loc.Get` resolves from the root resource map only; a module `.resw` serves `x:Uid` alone, and unpackaged MRT throws. Mirror every code-behind key into Deckle.App's `Resources.resw`. The module copy stays the wording source of truth.
- Use the Windows App SDK `ResourceLoader`, never the UWP one: only it works unpackaged.
- Every csproj with `.resw` declares `<DefaultLanguage>en-US</DefaultLanguage>`, or `x:Uid` values come up empty in other system languages.
- Never share one `x:Uid` across element types: MRT applies every `.resw` property to each element, and a mismatch crashes the page load.
