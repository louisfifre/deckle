---
name: deckle-modularite
description: When and along which lines to separate code into modules and files. Invoke before growing a module, judging a new one, or splitting a file or surface.
type: skill
---

# Deckle — Modularity

## Intent

Modularize an application so it stays legible and easy to grow: each capability a separable unit, with clean one-way dependencies.

## How

A module carries one responsibility nameable in a single sentence, and exposes a narrow public surface. More than one sentence, or a sprawling surface, means it is really several — separate them.

Know which kind of module you're writing before you grow it — the kind sets the rules. The dividing question: does it hold an active runtime state that acts on the system? If so, it's a domain module — it owns that state and everything attached to it, settings and persistence included. If not, it's support: pure code others call, the reusable bottom layer, referenced widely and depending on almost nothing. A module that owns no domain but assembles others is a shell when it only presents them, a host when its whole job is to aggregate by role — and for a host, referencing widely is the point, not a flaw.

Dependencies point one way only, toward the more fundamental — never sideways or up. A cycle means a shared notion must rise into a common module, or two modules are really one.

Make "could this live in its own file?" a reflex on every change — the answer is usually yes. Group related functions into small, cohesive, well-named files (a coherent cluster, never a single function stranded alone), many to a folder, subfolders for depth. A tree of named files shows what changed at a glance, and replaces both in-file section comments and thousand-line files. The only brake is performance: don't separate files when it would cost real load or runtime.

Settings are modular too: each module carries its own settings page and its persistence, instead of piling them into a central settings shell.
