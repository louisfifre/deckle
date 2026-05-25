---
name: deckle-modularite
description: Modularity and splitting doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries the criteria that guide where a module ends, when a file gets too big, what signals indicate that splitting should be reconsidered, and how to split a UI surface that has become monolithic. Triggers on phrases like deckle modularity, split deckle file, split deckle module, deckle modular refactor, deckle oversized file, deckle module responsibility, deckle module dependencies, deckle page splitting.
---

# Deckle — Modularity doctrine

## Role

Project-specific skill that answers two questions: **where a module ends**, and **when a file gets too big to remain comfortable**. Invoked before adding substantial code to a module, before deciding that a new module is needed, and before splitting a file that has become monolithic.

The doctrine targets two joint objectives. Make work with an LLM agent easier — it spots more readily which file is concerned when the tree is legible and files stay at a human size. And make later re-reading by Louis easier, step by step, without having to load files of several thousand lines into memory.

## Four-category taxonomy

Before reasoning about a module — where it ends, what it must expose, who can reference it — first identify which **structural category** it belongs to. The repo sorts into four, and the rules of one category are not those of the others.

**Support library** carries passive code with no runtime state of its own. Static code, structs, primitives, resources named by semantic key. Referenced widely, references almost nothing. No active singleton, no loop, no listening to Windows events. It is the lowest tier of the graph. Modules in this category today: `Deckle.Core`, `Deckle.Catalog`, `Deckle.Composition`, `Deckle.Chrono`.

**Domain module** carries a domain, an active runtime state, and often a Settings store + a Settings page. Active singleton that holds state across the app's lifetime and acts on the system — it listens to Windows events, reads a device, mutates the clipboard, holds a buffer, drives a loop. Most `Deckle.*` modules that do something are domain modules.

**Shell** is a presentation shell that receives and exposes nothing to domain modules. Does not reference domains, aggregates dynamically via a delegate registry or name-based resolution. `Deckle.Settings` is the only one of its kind today: its `NavigationView` loads pages owned by domain modules via `Type.GetType(tag)` from the `Tag` of the `NavigationViewItem`.

**Host** references widely, aggregates, serves a differentiated use. `Deckle.App` is the production host (EXE entry point, composition root that sees every module). `Deckle.Setup` is the first-run wizard host (transient, openable from Settings for re-execution). `Deckle.Playground` is the dev/tuning host (persistent, exposed via the tray). **Hosts are explicitly exempt from the modularity doctrine** — their role *is* to aggregate. `Playground` is not an ad-hoc exception that gets tolerated; it is an instance of a named category that also includes `App` and `Setup`.

## Discriminating criterion: support library vs domain module (K3)

When a module is a candidate between support library and domain module, the test: **does it carry an active singleton that holds state across the app's lifetime and acts on the system**? If yes, domain module. Otherwise, support library. This criterion is structurally stable — it does not depend on the future appearance of a Settings POCO or a UI page, it captures the real difference between passive utility and active actor.

The appearance of runtime state in a former support raises it to a domain module; this is a structural change worth naming. Conversely, removing the last runtime state from a domain module drops it back to support — that too is a structural change.

## Settings modularity doctrine

**The Settings page that configures a domain lives in the module that owns that domain, and its persistence service too.** Direct consequence: `WhisperPage` in `Deckle.Transcription`, `LlmPage` in `Deckle.Llm.Rewrite`, `AmbientPage` in `Deckle.Lighting.Ambient`. Pages still misplaced (typically `RecordingPage` on the audio capture side, `DiagnosticsPage` on the observability side) are historical residues to migrate.

The `Deckle.Settings` shell references no domain module — it is `Deckle.App`, as composition root, that sees everyone and wires them up. The shell aggregates pages from domain modules dynamically via a registry mechanism (today `Type.GetType(tag)`; planned evolution toward a static registry `SettingsHost.Register(SettingsPageDescriptor)` pushed by `App.OnLaunched` to guarantee the compile-time check).

## One responsibility per module

A Deckle module carries **one clear responsibility nameable in a single sentence**. If more than one sentence is needed to describe what a module does, it probably carries several and deserves to be split into sub-modules. Responsibility is expressed in domain or functional terms ("microphone audio capture", "transcribing an audio blob into text", "driving external lamps"), not in architectural terms ("the service", "the manager", "the helpers").

A well-split module has a **narrow public API** toward the rest of the app. Its implementation detail can be rich internally, but what it exposes is countable. When a module has dozens of public symbols, either it carries several responsibilities, or its public API is under-thought.

## Acyclic dependencies between modules

Dependencies form a **directed acyclic graph**: a module depends on modules lower in the hierarchy (more fundamental), never on modules at the same level or above. When a cycle appears, it is a signal of poor responsibility separation — either a shared notion should rise into a common fundamental module, or two modules should be merged because they are in reality a single thing.

The order of modules in the graph (from the fundamental leaves toward the host app) also reflects the logical order in which to work on them — leaves first, what depends on them next.

## File-splitting threshold

Beyond roughly five hundred lines, a file deserves to be examined. This is not a hard rule — an immutable configuration record can have a thousand lines without posing a problem, a glue file can be uncomfortable at two hundred. It is a **vigilance** threshold: past this point, look at whether the file carries a single responsibility or whether several semantic blocks cohabit.

When several semantic blocks cohabit, extracting them into separate files of the same module is almost always beneficial. The LLM agent spots more easily which file is concerned by its task, Louis sees in the list of touched files a more precise trace of what changed, and re-reading becomes manageable.

Extraction follows responsibility, not arbitrary quartering. A fifteen-hundred-line file split into two seven-hundred-and-fifty-line files unrelated to semantics adds nothing. A two-thousand-line file shattered into four five-hundred-line files each carrying a clear sub-role (the state machine, the callbacks, the instrumentation, the clean disposal) adds a great deal.

## Testability as a signal

A module that can be tested in isolation, without depending on the full execution environment (a real WinUI window, physical hardware, an external service), is probably well split. The domain logic lives in pure classes that depend only on interfaces; the implementations that touch the real world are injected and can be replaced by doubles in tests.

Conversely, when a module cannot be tested without booting the entire app, this is a signal that responsibilities are mixed — pure logic is entangled with platform coupling. The refactor consists in extracting the pure logic into a fundamental module that does not depend on the platform, and leaving the platform layer as a thin facade.

## Splitting into sub-pages for windows that grow

When a window or a page accumulates different modes (a home mode, a calibration mode, a monitoring mode, an advanced configuration mode), the single code-behind becomes uncomfortable. The natural pattern is to **split into navigable sub-pages** — each mode becomes an autonomous page, the window becomes a navigation frame that selects the active page. This is what canonical Windows surfaces do for rich windows.

Splitting by sub-pages has a positive side effect: it makes each mode testable in isolation, because the sub-page can be instantiated without the host window. And it takes the modularity doctrine out of the pure-code domain and applies it to UI surfaces.

## Logic versus presentation distinction

In a module that carries both logic and a UI surface, **separate the two sides**. A page's code-behind must not carry domain logic — it does the wiring between the visual surface and a domain object (a view-model or a service) that carries the logic. This discipline makes the logic testable and keeps the UI page thin. When domain logic is found in a `.xaml.cs` file, it is generally a sign that it should be extracted into a dedicated object.

## How to split in practice

When a file or a module deserves to be split, the following sequence is effective.

First, **identify the semantic blocks** present in the file — the internal roles that the global responsibility covers. An agent can help with this mapping by reading the file and producing a candidate split. At least three or four identifiable blocks are needed for a split to make sense.

Then, **start with the most independent blocks** — those with the least coupling to the others. A static helper that reads no instance field can leave on its own; a nested class that depends on its enclosing class only through an interface can leave with its interface. The most coupled blocks remain in place in the original file until their extraction becomes possible.

Finally, **after each extraction, validate that the code compiles and behaves as before**. The build discipline passes at each step, not only at the end. A regression introduced by a split is easier to fix immediately than after ten cumulated splits.

## Pointers

- **`deckle-refonte`** — orchestrator skill that points to this skill when a workstream touches the modularity strand.
- **`deckle-docs`** — when a non-trivial split decision is made (notably the extraction of a notion into a new module), it deserves an entry in the relevant module's journal.
- **`deckle-logging`** — observability of a well-split module is clearer; when splitting, take the opportunity to verify that observation sites follow the new boundary.
