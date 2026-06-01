---
name: deckle-modularite
description: Modularity doctrine for Deckle: why and when to separate code into modules and files — one responsibility per module, acyclic dependencies, the ~500-line vigilance threshold, the splitting strategy. Invoke before growing a module, judging a new module, or splitting a file or UI surface. Triggers like deckle modularity, split file, split module, oversized file, module responsibility, page splitting.
type: skill
---

# Deckle — Modularity doctrine

## Role

Project-specific skill answering **why and when we separate code into modules and files** — not which modules exist (the code and `TREE.md` are the registry). Invoked before adding substantial code to a module, before judging that a new module is warranted, and before splitting a file or surface that has grown monolithic.

Three payoffs drive the doctrine. An **agent** finds the right file faster when the tree is legible and files stay human-sized. **Multi-agent collaboration** stays untangled when ownership is clear and maximally-split files keep diffs readable. And **re-reading later** never means loading thousands of lines at once.

## One responsibility per module

A module carries **one responsibility nameable in a single sentence**, expressed in domain terms ("microphone audio capture", "driving external lamps"), never architectural ones ("the service", "the helpers"). More than one sentence means more than one responsibility — split. Its public API toward the rest of the app stays **narrow**; dozens of public symbols signal either several responsibilities or an under-thought surface.

## Four structural categories

Before reasoning about a module, identify its **structural category** — the rules of one are not those of another. This is also the reuse doctrine: what gets shared, and what stays put.

- **Support library** — passive code with no runtime state of its own (statics, structs, primitives, keyed resources). Referenced widely, references almost nothing; the lowest tier of the graph. This is the *shared, reusable* layer.
- **Domain module** — owns a domain and an active runtime state: a singleton living across the app's lifetime that acts on the system (listens to Windows events, reads a device, holds a buffer, drives a loop). An actor that does work, not shared utility code.
- **Shell** — a presentation shell that references no domain; it aggregates them dynamically (name-based resolution, a registry).
- **Host** — references widely and aggregates by role (production entry point, first-run wizard, dev/tuning surface). **Hosts are exempt from the modularity doctrine — aggregating is their job.**

The support↔domain frontier is the one that drifts. The test: **does it hold an active runtime state that acts on the system?** Yes → domain; no → support. State appearing in a former support promotes it; losing its last state demotes it — either way a structural change worth naming.

## Acyclic dependencies

Dependencies form a **directed acyclic graph**: a module depends only on more fundamental ones, never sideways or upward. A cycle signals poor separation — either a shared notion must rise into a common fundamental module, or two modules are really one and should merge. The graph order (fundamental leaves → host) is also the order to work in: leaves first.

## Settings live with their domain

The settings page that configures a domain, and its persistence, **live in that domain's module** — not in the settings shell. The shell owns no domain; it aggregates pages dynamically, and the composition root wires everyone up. A page found outside its domain is a residue to migrate.

## File size is a vigilance threshold

Past ~500 lines, **examine** the file. Not a hard limit — an immutable record can run to a thousand without harm, a glue file can be uncomfortable at two hundred — but a prompt to ask: one responsibility, or several blocks cohabiting? When several cohabit, extract them into separate files of the same module. Extraction follows **responsibility, not arbitrary quartering**: a 1500-line file halved into two unrelated 750-line files gains nothing; shattered into four files each carrying a clear sub-role (the state machine, the callbacks, the instrumentation, the disposal) gains a lot. Default to splitting, toward specific files.

## Testability is a signal

A module testable in isolation — without a real window, hardware, or external service — is probably well split: pure logic depending only on interfaces, real-world implementations injected and replaceable by doubles. A module that needs the whole app booted to test signals mixed responsibilities — extract the pure logic into a platform-free module, leave a thin facade.

## Splitting UI surfaces

A page or window that accumulates modes (home, calibration, monitoring, advanced) outgrows a single code-behind. Split it into **navigable sub-pages** — each mode an autonomous page, the window a frame that selects the active one, the way canonical Windows surfaces do. Side benefit: each mode becomes testable without its host window.

In any module carrying both, **separate logic from presentation**. A `.xaml.cs` wires the surface to a domain object (view-model or service) that holds the logic; domain logic found in code-behind is a sign to extract.

## How to split

1. **Map the semantic blocks** — the internal sub-roles the responsibility covers. Three or four identifiable blocks make a split worthwhile.
2. **Start with the most independent** — a static helper reading no instance field leaves first; the most coupled blocks stay until their extraction becomes possible.
3. **Validate after each extraction** — it compiles and behaves as before. The build discipline holds at every step, not only at the end.

## Naming

Modules follow `Deckle.<Capability>[.<Sub>]`, the hierarchy expressing a consumer/sub relationship (`Deckle.Lighting.Ambient` — Ambient consumes Lighting). The full naming doctrine lives in `deckle-nomenclature`; the live module list lives in the code and `TREE.md`.

## A standing caveat

This spirit is not yet applied everywhere — some modules still deserve refactoring toward it. Treat an oversized or hard-to-test module as a known debt to repay when it is next touched, not as the norm to imitate.

## Pointers

- **`deckle-nomenclature`** — how modules, namespaces, and symbols are named.
- **`session-save-context`** — route a non-trivial split decision (notably extracting a notion into a new module) through the cascade for a durable trace.
- **`deckle-logging`** — when splitting, verify that observation sites follow the new boundary.
