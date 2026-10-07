---
description: "Behavior and composition contracts of the reusable terminal launcher beneath Deckle Scripts: product, modules, input, views, preparation, execution, rendering, reuse."
type: module-specification
---

# Terminal interaction specification

The contracts of the reusable terminal launcher beneath Deckle Scripts. Read it before designing, implementing, or reusing the launcher's views and interaction primitives. `CONTEXT.md` governs the vocabulary; this document governs behavior and composition, written in the indicative: every sentence is a rule unless it says otherwise. A term the glossary does not hold is defined in bold where it first appears.

## Product

The launcher is one backbone that gathers every script of a repository, so its maintainer finds and runs them without an LLM. The Action Menu is the exhaustive inventory of the repository's scripts: nothing stays reachable only from the command line, and folders group rarely used scripts rather than hiding them. Every workflow command also remains callable on its own from the command line.

A repository keeps its scripts under its own `scripts/` subfolders and the menu links to them; the backbone ships the standard commands every repository needs, such as scans. The backbone is a distributable product, installable into another repository at its creation and usable by other people. Wiring a script into the menu is understandable by a person and by an agent, and an installer that meets an existing setup adjusts it rather than replacing it. The installation mechanism and automatic updates are open.

The runtime is PowerShell on Windows. The backbone is written for Windows PowerShell 5.1, which every Windows ships, so it also runs under PowerShell 7. A port to another runtime waits for a real need.

## Modules

```text
                              Deckle Launcher
                              /             \
                             v               v
              Interaction Compositions   Execution Runtime ---> Workflow commands
                             |
                             v
                      Interaction Core
                        /           \
                       v             v
          Interaction Renderer   Terminal Host
```

Arrows are the only code dependencies: a destination never imports, calls, or inspects its source. Descriptor, snapshot, input-event, and render-plan types are shared public types, not dependencies in this sense. The Launcher also calls the Core's public facade to submit handler decisions and Execution projections; the Core never calls back.

- **Terminal Host** — one console host behind normalized capabilities, input events, and drawing operations. It owns the reversible platform state (alternate buffer, cursor, input mode, color, viewport, pointer registration) and restores every state it changed on any exit, including an exceptional one. It knows nothing of Views, Actions, Executions, repositories, or engines.
- **Interaction Core** — one structured session: the current View, the navigation stack, the focused target, keyed paging positions, and the render cycle. It resolves normalized input against the focused target into explicit transitions. Repository state and the authoritative Execution stay outside it and enter as immutable projections. It invokes no repository work and infers nothing from labels, punctuation, colors, positions, or presentation roles.
- **Interaction Compositions** — pure builders and reducers over public Core descriptors that turn the semantic model into View definitions: Persistent Header, Action Menu, Preparation, Execution View, Panels, Selectors, Review, Confirmation, Logs. They declare intents and execute none, and contain no Deckle workflow code. A consumer uses the whole library or only the compositions it needs.
- **Interaction Renderer** — a pure projection of View descriptors, retained state, terminal metrics, theme, and capabilities into a render plan of clipped display cells and drawing operations. It owns layout, reflow, display-cell measurement, clipping, theme resolution, and the separator hierarchy. It reads no input, keeps no state, navigates nowhere, and never writes to the console: the Host alone executes its plan.
- **Execution Runtime** — one active Execution: its frozen request, the child-process lifecycle, the captured Journal, Tracking, and the structured completion, published as immutable `Started`, `Journal updated`, `Tracking updated`, and `Completed` updates. It consumes a repository-supplied **execution adapter** that declares engine requirement, profile behavior, working directory, elevation, executable, argument values, and the meaning of its output; none of these is inferred from another. The Runtime validates the declaration, selects a compatible engine, builds the invocation through the engine's API rather than string concatenation, captures the streams, and observes the exit. It depends on nothing interactive.
- **Deckle Launcher** — the shell that presents one repository's workflows: banner and context, the catalog of Actions and Accesses, Preparation controllers, intent handlers, engine requirements, and the presentation of action-owned output. Workflow commands never import the launcher or the interaction modules; the backbone ships a separate **result library** they may use to report their conclusion.

## Public surface

The framework exposes responsibilities, never rendering mechanisms: start and close a session; describe a View and its body; publish stable target descriptors and the commands currently available; apply a command and return a transition; apply an immutable external update; render retained state for the current metrics. The module facade is the only supported import. Consumers never build rows, coordinates, ANSI sequences, or key records.

A target descriptor carries a View-local stable `TargetId`, an `IntentKind`, an immutable payload, an enabled state with an optional reason, and a presentation role. The role never determines the intent. Activating an enabled target emits an **Intent Request** (source View and target, kind, payload) to the Launcher, which invokes the registered handler. The handler answers with a **Transition Decision**, which the Launcher submits to the Core, or an **Execution Request**, which the Launcher starts through the Runtime after installing the Execution View. Activating a disabled target keeps the View and shows the reason.

An Execution Request names the Action and its Variant, carries the exact reviewed Preparation revision, or an immutable input snapshot for an Action without Preparation, and selects the execution adapter.

## State

Exactly one View is current, and its retained state is the only source of every redraw; the terminal contents are never state. Every interaction yields one outcome: Stay, Open (push a View), Replace (swap the current View so that Back reaches its caller), Back, Cancel (discard the unaccepted state of a transient interaction), Request Intent, or Exit. Drawing never navigates and never executes work.

Opening an Access pushes a View; Back restores the caller with its focus, Selections, and paging intact. A View opens only for another durable context: focus moves, Selector editing, validation messages, paging, and reflow never open one.

## Input

Enter and the arrow keys are the complete input set. Arrows move focus through the composition's semantic order; Enter activates the focused target, which for a Checkbox or a Radio Button means toggling it. Everything else is a shortcut or an alternative:

| Input | Command |
|---|---|
| Space | Toggle the focused option |
| Backspace, visible Back control | Return by one View; never exits |
| Escape | Cancel the transient interaction that owns input; otherwise Back; at the root Action Menu, Exit |
| Ctrl+C | Emergency exit: quiesce the child process, restore the terminal, leave |
| Page Up, Page Down, mouse wheel | Previous or next page of the targeted Panel |
| Home, End | First or last page |

A visible Back control exists in every nested View. It is a Navigation Control, not an Action: its label carries a leftward marker independent of focus, and at a given geometry it inherits the first option track and cell width of its owning Action Menu rather than a grid of its own. The root Action Menu ends with a Quit target whose intent is Exit.

A focused text editor consumes characters, Space, Backspace, Delete, Home, End, and the horizontal arrows before View commands; Escape cancels the edit without accepting its buffer.

Paging is the only overflow interaction. Every paged Panel is focusable and shows Previous and Next targets whenever more than one page exists, so every page is reachable with arrows and Enter; when an activated direction reaches its boundary, focus moves to the other paging target. Pages run chronologically: Next shows newer content. A wheel event targets the Panel under the pointer when coordinates are available, otherwise the focused paged Panel. Wheel paging is required wherever the host reports pointer input; its indication appears beside Page Up and Page Down.

Global Command Indications are generated from the active bindings as key-and-label pairs in the Header's right-hand space, grouped by spacing, and hide by priority on a narrow terminal: activation and editing commands first, then Back, movement last. Scrolling Command Indications sit at the bottom, framed by a local separator, only while the View or Panel pages, and name the targeted Panel. A View never advertises a command it cannot honor.

## Focus and presentation

A View with interactive content has exactly one focused target, which survives redraw and resize. Every ordinary target shares one identical focus treatment. Danger and Exit keep their distinct treatment: both are red at rest; a focused Danger target inverts to white on dark red, a focused Exit target takes the ordinary treatment. Color is never the only carrier of focus, checked, disabled, danger, or completion: each state has a structural or textual mark, and without color the focus cell carries `>`. A disabled target stays visible, carries a structural marker, and shows its reason when one is declared.

Presentation and intent are independent. Descriptors say what an object is; the renderer derives its presentation role; the theme maps role and state to terminal attributes, and callers never supply colors or layout variants. The role inventory is stable: banner, context, titles, separators, Action, Variant and body, Access, Navigation, supporting text, Exit, Danger, outcomes, command key and command label. The default theme is the Figma `22:27` palette in its light and dark variants; per-role customization from a repository configuration is a later capability. Journal content keeps its own admitted presentation and is not remapped.

## Views

A View is one Persistent Header over one View Body. The Header owns two rails, banner then current context, the Global Command Indications in the remaining right-hand space of either rail, and one primary separator; indications never add a rail or displace the context. The primary separator is distinguished from local ones by structure, not by tone alone. Sections use lighter separators or whitespace; no full-width rule per Section or item.

The View Body composes an Action Menu, a Preparation, and Panels, together or apart. Rows and columns are layout results, never semantic children. A Panel owns one content responsibility, keeps its identity when reflow moves it, retains its paging state in the Core under that identity, and is titled by its content, never by a generic `Results`.

An Action Menu organizes Sections, Actions, Action Rows with their Variants, and Accesses in a stable semantic order that reflow never changes. A folder of scripts is an Access. An Access label ends with an ellipsis as the non-color cue that a View will open; its descriptor, not the punctuation, carries the intent. The main Action Menu ends with a Logs Access above Quit.

## Preparation

A Preparation keeps its Selectors, Effective Scope, Review, and Confirmation in one View Body, paginating vertically when height requires while retaining every Selection and the focused Selector. A Selector declares whether it accepts one value, several, free text, or a bounded edit; multi-selection is state inside one Selector. Filters remain a low-priority reusable system of Checkbox and Radio Groups that any Action can declare.

The Launcher supplies one **Preparation Controller** per Action. It owns accepted Selections, creates an immutable and distinct revision after every change, resolves Effective Scope from the current Selections and the execution target, validates, and publishes the snapshot. A result for a stale revision is discarded; a resolution failure appears beside the Preparation content, never as a View of its own. Every change invalidates the Review; Review and Confirmation reference the same revision; Confirmation is unavailable while resolution is pending or failed; Execution starts from that exact revision.

An Action may declare that its Confirmation is a separate step between Review and Execution rather than an inset of the Preparation. That step names the effect and the Effective Scope, distinguishes the safe choice, focuses it first, requires deliberate activation of the other choice, and is cancelled by Escape.

## Execution

Execution begins only from an Execution Request; the Runtime freezes that revision for one run, and later edits or a restart create another Execution. The flow is Action Menu, Preparation, the confirmation step when declared, Execution. Execution replaces its Preparation, so Back after completion returns to the owning Action Menu; an Action without Preparation opens its Execution directly over the menu. In the Execution View, the Header and its separator remain, the Action Menu disappears, and the body shows the Back control followed by the Journal and Tracking Panels.

Runtime updates arrive asynchronously; focus and Journal paging stay usable during a run. An analysis, scan, or statistics Execution can be cancelled from the launcher through a declared command shown while available: the child stops, the result is cancellation, nothing partial is shown, and the launcher returns to the menu ready to relaunch. Whether other Actions can be cancelled is undecided; while a run that cannot be cancelled is active, Back, Backspace, and Escape are disabled, the Header does not advertise them, and Tracking says the run must finish. Ctrl+C remains the emergency exit in every case: the Runtime forwards an interrupt, waits for the child to exit or to reach its declared forced-termination boundary, publishes cancellation or failure, and only then lets the terminal restore.

The Execution Journal Panel and the Execution Tracking Panel are independent. On a wide terminal the Journal takes about five-sixths of the width and Tracking one-sixth, chosen from measured minimum widths so Tracking stays readable; on a narrow one, a height-limited Journal stacks above Tracking. Tracking wraps; Journal lines never wrap and are clipped to the available cells. A running Journal follows its latest page; a completed Journal reopens at its latest page; a Report, a Review, and guidance open at their beginning.

The Journal retains complete structured records in admission order per stream, with no cross-stream order promised: time, source, stream, logical content, presentation segments. Output is captured, never passed through, so a child cannot draw across the Tracking Panel; ConPTY remains an optional host strategy. Only SGR presentation semantics (colors, intensity, emphasis, reset) are retained; every other control sequence is discarded, and presentation resets at every rendered line. A carriage return replaces the provisional progress record until a newline commits it.

Tracking is Deckle-owned state: significant steps, current state, elapsed time where useful, and the Execution Result. The executor publishes a structured conclusion; free text such as `Result : Success` is compatibility evidence, never the canonical source. The result vocabulary is success, failure, partial completion, skipped work, and cancellation, and the result library gives a script ready-made means to report each.

An Execution may also produce **action-owned output** of two kinds: a **Report**, shown on screen, or an **Artifact**, written to disk (`TREE.md` is one). The adapter returns it, the Runtime transports it unchanged in `Completed`, and the Launcher presents it with or after the Execution View; the Core never relabels it as the Execution Result, and the framework never invents a continuation the workflow does not implement.

The session keeps every Execution Journal produced since the launcher started, in memory, unfiltered, until exit. The Logs Access opens a plain paginated list of the runs and their results; opening a run reopens its Journal at its latest page.

## Rendering

Rendering is a deterministic projection of retained state, metrics, theme, and capabilities; a complete redraw is valid whenever geometry or capability changes, continuous resize animation is not a goal, and redundant redraws are avoided. The launcher uses the full terminal width with no cap: content stays left-aligned and right-aligned elements stay right. A narrow terminal reflows compositions vertically, in their semantic order, before any resize message; below the floor where nothing fits, still to be measured, the renderer presents one resize state rather than a partial View. One layout serves each use case; a new layout appears only when a use case breaks several existing rules.

Resize preserves the current View and stack, the focused target, Selections, Execution state and Journal, and the nearest valid page of each Panel. Layout measures display cells, not string length; clipping accounts for presentation sequences and should account for wide and combining characters. The session leaves the terminal valid after narrow dimensions, interrupted drawing, an exception, or exit.

## Capabilities and engines

The Host probes capabilities instead of trusting a PowerShell version, a terminal brand, or a parent process. Each reports Supported, Unsupported, or Unknown; at minimum: interactive input and output, width and height, cursor addressing and clear, color and safe VT presentation, alternate buffer, pointer input. Degradation is explicit: without color, markers carry every state; without an alternate buffer, the main buffer is used and restored without erasing prior content; without cursor addressing, the launcher refuses to start with one static explanation; without pointer input, the keyboard path is complete; every changed mode returns to its observed prior value.

The backbone and its bootstrap parse and import under Windows PowerShell 5.1, from sources in an encoding it decodes deterministically; PowerShell 7 syntax stays behind a file or process boundary that 5.1 never reads. An Action declares when it requires PowerShell 7: under 5.1 it stays visible, disabled with that reason, or delegates to an installed `pwsh`.

## Reuse

The reusable modules take one injected launcher context as input: branding, context, catalog, labels, intent handlers, Preparation controllers, execution adapters with their engine requirements, Tracking steps, and the presentation of action-owned output. They contain no Deckle path, branch, command name, workflow assumption, or output label, and never call a repository script directly. A consumer edits no internal file and depends on no launcher global; Exit and other control flow cross the facade as public transitions, never as exceptions. A repository-neutral fixture proves reuse until a second consumer does.

Tests assert these contracts through the public surface; renderer tests may add pure layout, clipping, and separator invariants. Host acceptance covers Windows Terminal, conhost, and the supported IDE terminal under Windows PowerShell 5.1 and PowerShell 7.
