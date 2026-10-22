---
description: Root agent-instructions for Deckle — identity, hard rules, posture, and where the rest lives.
---

# AGENTS.md — Deckle

## Identity and goals

Deckle is a local Windows utility gathering several complementary subsystems behind one entry point — hotkey voice transcription (delivered), ambient lighting (in progress), future assistance modules. Not a single-purpose tool.

Two founding goals gate every decision. **Learning to develop** — understanding what we build outranks the fact that it works. **Emancipation from paid services** — local, autonomous, no structural cloud dependency. Any proposal that drifts from these two justifies it explicitly.

Quality target: a Windows app at Microsoft first-party level. Sensory reference: Windows 11 Settings, Explorer, PowerToys. Every UI/platform choice passes « would Microsoft ship this in an official Store app? » — if no, start over.

## Hard rules

Local validation is compile-only `dotnet build` with `/m:4`, Debug x64 by default, without stopping or relaunching Deckle. A branch merges into main once the session merging it has run the test suites of the modules the branch touched and they are green. `Directory.Build.rsp` runs every command-line build at low priority and disables MSBuild node reuse; do not bypass it with `-noAutoResponse`. The .NET CLI applies its default maximum node count after response files, so every build or publish invocation must keep `/m:4` explicit. Never run two builds at once, whatever the session: when another `dotnet build` is running on the machine, wait for it. `publish` stays the maintainer's act, never triggered by agents.

Terminal-interface inspection uses deterministic `scripts/deckle-preview.ps1 -Snapshot ...` output. Agents MUST NOT start or drive the interactive `scripts/deckle.ps1` launcher or invoke its actions; that daily launcher may stop or relaunch the resident app by design.

The commit ships under the maintainer's sole identity — no `Co-Authored-By: Claude` trailer, no `🤖 Generated with Claude Code` line.

A context element that will come back into every later session (an `AGENTS.md`, a skill, an ADR, a memory) is written to disk only once the maintainer has agreed to it.

Personal data read from an Anytype space (Home) is runtime data. It is never compiled into code, never copied into a fixture, a documentation example or a log, and never sent to a remote model or service: only local inference and the Anytype backend's own sync process it.

Speech models, ASR and TTS alike, run in BF16 or FP16. A 4-bit quantization serves a smoke test only, never a shipping candidate: on French speech it loses a fidelity the automatic metrics barely register.

## Bug notes and regression tests

Once a confirmed bug's cause is established, invoke `deckle-bug-note` before closing the workstream. The fix and its regression test follow `deckle-testing`.

## Name the hat

Before any non-trivial code, name the posture(s) framing the answer — engineer (architecture, patterns, threading, perf, tests), WinUI 3 expert (XAML, controls, backdrop, theme resources, DWM), designer (layout, hierarchy, rendering), product manager (the what, the order, for whom). A request may need several at once.

## Where the rest lives

Everything specialized is discoverable, not pre-chewed here — orient, never recopy.

- "How we do things" doctrines → `deckle-*` skills in `.agents/skills/`; their descriptions say when to invoke them, go look.
- A module's own why, traps and constraints → the comments in its code, at the line they concern.
- Repo structure (tree, headers, skills) → `TREE.md`, auto-generated.
- Frozen decisions → `docs/adr/`.
- WinUI/XAML and Windows APIs → Microsoft Learn MCP before local code.

## Worktrees

Use a sibling `worktrees/deckle` container beside the directory that holds the
primary checkout. For example, `D:\projects\deckle` resolves to
`D:\worktrees\deckle`; derive it from the checkout instead of assuming a drive.
