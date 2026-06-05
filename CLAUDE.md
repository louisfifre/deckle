---
description: Root agent-instructions for Deckle — identity, hard rules, posture, and where the rest lives.
type: agent-instructions
---

# CLAUDE.md — Deckle

## Identity and goals

Deckle is a local Windows utility gathering several complementary subsystems behind one entry point — hotkey voice transcription (delivered), ambient lighting (in progress), future assistance modules. Not a single-purpose tool.

Two founding goals gate every decision. **Learning to develop** — understanding what we build outranks the fact that it works. **Emancipation from paid services** — local, autonomous, no structural cloud dependency. Any proposal that drifts from these two justifies it explicitly.

Quality target: a Windows app at Microsoft first-party level. Sensory reference: Windows 11 Settings, Explorer, PowerToys. Every UI/platform choice passes « would Microsoft ship this in an official Store app? » — if no, start over.

## Hard rules

Local validation is compile-only `dotnet build`, Debug x64 by default, without stopping or relaunching Deckle. `publish` stays the maintainer's act, never triggered by agents.

The commit ships under the maintainer's sole identity — no `Co-Authored-By: Claude` trailer, no `🤖 Generated with Claude Code` line.

## Name the hat

Before any non-trivial code, name the posture(s) framing the answer — engineer (architecture, patterns, threading, perf, tests), WinUI 3 expert (XAML, controls, backdrop, theme resources, DWM), designer (layout, hierarchy, rendering), product manager (the what, the order, for whom). A request may need several at once.

## Where the rest lives

Everything specialized is discoverable, not pre-chewed here — orient, never recopy.

- "How we do things" doctrines → `deckle-*` skills in `.claude/skills/`; their descriptions say when to invoke them, go look.
- A module's local technical doctrine → the `CLAUDE.md` at that module's root.
- Repo structure (tree, headers, skills) → `TREE.md`, auto-generated.
- Frozen decisions → `docs/adr/`.
- WinUI/XAML and Windows APIs → Microsoft Learn MCP before local code.
