---
description: Root agent-instructions for Deckle — identity, hard rules, posture, and where the rest lives.
---

# AGENTS.md — Deckle

## Identity and goals

Deckle is a local Windows utility gathering several complementary subsystems behind one entry point — hotkey voice transcription (delivered), ambient lighting (in progress), future assistance modules. Not a single-purpose tool.

Two founding goals gate every decision. **Learning to develop** — understanding what we build outranks the fact that it works. **Emancipation from paid services** — local, autonomous, no structural cloud dependency. Any proposal that drifts from these two justifies it explicitly.

Quality target: a Windows app at Microsoft first-party level. Sensory reference: Windows 11 Settings, Explorer, PowerToys. Every UI/platform choice passes « would Microsoft ship this in an official Store app? » — if no, start over.

## Hard rules

Build and test through `scripts/build-locked.ps1 [-Test] -Project <csproj>`, Debug x64 by default: it carries the project's build flags and holds the machine to three builds at once.

The session that merges a branch into main runs the test suites of the modules it touched and builds `Deckle.Tests.sln` on the merged tree, then merges, records the version and runs `scripts/deploy-prod.ps1`, which builds main in Release, self-checks it and stages it for the next start of the maintainer's Deckle. A failed deploy leaves the prod copy as it was; repairing main comes first.

The remote, origin, holds the main every machine starts from: a merge into main begins from origin's main and ends by pushing main and its version tag. Giving a version to other people is the maintainer's decision: a GitHub release is published on his word. The running Deckle is his too: an agent never stops or starts it, and sees the app through `scripts/self-check.ps1`, a separate instance on its own data root.

The launcher, its library, its Settings folder and the packs under `scripts/` are installed from the deckle-scripts repository; `scripts/.deckle-scripts/manifest.json` names the installed files. They are never edited here: an improvement goes to that repository, and applying its update stays the maintainer's act.

A version is an annotated `vX.Y.Z` tag, recorded on the merge commit at each merge into main with `scripts/git/record-version.ps1 -Bump <part> -Apply`; no file carries the number, and a build reads the highest tag reachable from its HEAD (`deckle-versioning`).

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
- Which script to run (build, test, self-check, prod, processes, logs) → [`scripts/INVENTORY.md`](scripts/INVENTORY.md), generated from the scripts' Headers; when none fits, use your own command. What is installed and what is Deckle's own → `scripts/README.md`.
- Frozen decisions → `docs/adr/`.
- The app as a person sees it → `scripts/self-check.ps1`, whose page captures a review pass reads.
- WinUI/XAML and Windows APIs → the `microsoft-docs` skill before local code.

## Worktrees

Use a sibling `worktrees/deckle` container beside the directory that holds the
primary checkout. For example, `D:\projects\deckle` resolves to
`D:\worktrees\deckle`; derive it from the checkout instead of assuming a drive.
