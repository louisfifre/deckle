---
description: "Normative contracts for Deckle's reusable terminal-interaction system: module boundaries, view composition, interaction state, rendering, and first-cycle scope."
type: module-specification
---

# Terminal interaction specification

This specification defines the reusable terminal-interaction system beneath Deckle Scripts. Read it before designing, implementing, or reusing the launcher's views and interaction primitives.

`CONTEXT.md` governs the vocabulary. This document governs behavior and composition. Examples illustrate the contracts without narrowing them to Deckle's current workflows.

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** express normative weight.

## Purpose

The system presents repository workflows through one understandable terminal interface while keeping workflow commands independently usable from the command line. Its reusable framework MUST work for another repository without carrying Deckle actions, paths, branding, or release policy with it.

The first cycle provides the structure used every day:

- one controlled terminal session;
- a Persistent Header and composable View Body;
- Action Menu, Preparation, and Execution View compositions;
- normalized focus, activation, selection, navigation, cancellation, paging, and exit commands;
- captured execution output with separate Journal and Tracking responsibilities;
- deterministic redraw from retained state;
- launcher and daily-action compatibility with Windows PowerShell 5.1 and PowerShell 7.

The first cycle retains Executions in memory only. Global Logs, execution history, durable run identity, Rerun, Retry, user cancellation that keeps the launcher open, fine-grained scrolling, general disabled-action explanations, and ConPTY are later capabilities. Emergency session exit still quiesces the active child before restoring the terminal. Cross-repository packaging, installation, and automatic updates are also deferred.

The design MUST leave those capabilities additive. Their future insertion points MAY be reserved in the composition grammar, but the first cycle MUST NOT render empty placeholders for absent features.

## Module map

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

Arrows show primary code dependencies. A destination MUST NOT import, call, or inspect its source. The Deckle Launcher also calls the Interaction Core's public facade directly to submit handler decisions and immutable Execution projections; this coordination dependency does not allow the Core to call back into the Launcher.

### Terminal Host

The Terminal Host translates between one console host and normalized terminal capabilities, input events, and drawing operations.

It owns the reversible platform state needed by a terminal session: alternate buffer, cursor visibility, input mode, color capability, viewport dimensions, and pointer-input registration. It MUST restore every state it changes when the session ends, including exceptional exit.

It MUST NOT know about Views, Actions, Preparations, Executions, repository paths, command names, or PowerShell engines. Console-host differences belong behind this boundary.

### Interaction Core

The Interaction Core runs one structured interaction session.

It owns the current View snapshot, navigation stack, focused target, keyed paging positions, and render cycle. It resolves normalized physical input according to the focused target and applies explicit state transitions. Repository-owned state and the authoritative Execution are not Core state; immutable projections enter through its public update seam.

It MUST NOT invoke repository commands or infer semantics from labels, punctuation, colors, row positions, or presentation roles.

### Interaction Compositions

Interaction Compositions turn the shared semantic model into reusable View definitions.

They provide the Persistent Header, Action Menu, Preparation, Execution View, Panels, Selectors, Review, and Confirmation compositions. A Composition is a pure builder and reducer over public Interaction Core descriptors and state snapshots. It declares behavior through explicit interaction intents and does not execute their handlers.

They MUST NOT depend on Deckle workflow implementations. Another repository MAY use the complete composition library or only the compositions it needs.

### Deckle Launcher

The Deckle Launcher is the shell that presents Deckle's repository workflows.

It owns the Deckle banner and context, the catalog of Actions and Accesses, workflow-specific Preparation controllers, intent handlers, Action engine requirements, domain-specific outputs, and the mapping from completed work back to its owning Action Menu.

Workflow commands MUST remain independently callable and MUST NOT import the launcher or terminal-interaction modules.

### Execution Runtime

The Execution Runtime owns one active Execution: its frozen request, child-process lifecycle, captured Journal, Tracking state, and structured completion. It consumes a repository-supplied execution adapter and emits immutable `Started`, `Journal updated`, `Tracking updated`, and `Completed` updates. `Completed` carries the structured conclusion and MAY carry a repository-owned output value without interpreting or relabeling it.

The adapter declares engine requirement, profile behavior, working directory, elevation policy, executable, argument values, and the meaning of its returned output. The Runtime validates that declaration, selects a compatible engine, quotes and constructs the process invocation, captures streams, and observes exit. It MUST NOT depend on the Interaction Core, Interaction Compositions, or terminal drawing. The Deckle Launcher passes its updates into the Interaction Core as external View-state projections and interprets any completed repository-owned output.

### Interaction Renderer

The Interaction Renderer is a pure support module. It projects View descriptors, retained snapshots, terminal metrics, theme, and capabilities into a render plan containing clipped display cells and drawing operations.

It owns layout, responsive reflow, display-cell measurement, clipping, theme resolution, and separator hierarchy. It MUST NOT read input, retain session state, navigate, invoke work, or write to the console. The Terminal Host alone executes its render plan.

## Public contract

The reusable framework's public surface MUST expose responsibilities rather than rendering mechanisms:

- start and close one interaction session;
- describe a View and its body compositions;
- publish stable target descriptors and the normalized commands currently available;
- apply an interaction command and return a transition;
- apply an immutable external state update;
- render retained state for the current terminal metrics.

Consumers MUST NOT construct renderer rows, cursor coordinates, ANSI cursor movement, or terminal-specific key records. The module facade MUST be the only supported import surface; internal files MAY change without changing consumers.

Every interactive target descriptor contains a View-local stable `TargetId`, an `IntentKind`, an immutable payload, an enabled state, an optional disabled reason, and a presentation role. Presentation role MUST NOT determine intent. Activation of an enabled target emits its declared intent; activation of a disabled target keeps the View and exposes its reason.

Activation returns an `Intent Request` from the Interaction Core to the Deckle Launcher. The request contains the source View and Target identities, Intent Kind, and immutable payload. The Launcher resolves and invokes the registered handler. A handler returns either a `Transition Decision` or an `Execution Request`; the Launcher submits a Transition Decision to the Core facade, or installs the Execution View through that facade and starts the Execution Request through the Runtime. The Core never calls a handler or emits repository work itself.

An Execution Request contains the owning Action and Action Variant identifiers, the exact immutable reviewed Preparation revision when one exists, and the selected execution adapter. A direct Action without Preparation carries an immutable Action input snapshot instead.

## State and transitions

### Single source of visible state

Exactly one View is current. Its retained state is the source for every redraw. The terminal contents themselves MUST NOT be treated as state.

Every interaction produces one explicit outcome:

- **Stay** — update state within the current View;
- **Open** — push and present another View;
- **Back** — close the current View and restore its caller;
- **Cancel flow** — discard unconfirmed flow state and return to the owning Action Menu;
- **Request Intent** — emit the focused target's declared intent to the Deckle Launcher;
- **Exit** — close the complete interaction session.

A renderer MUST NOT navigate or execute work as a side effect of drawing.

### Navigation state

Opening an Access pushes a View. Back restores the prior View with its focus, Selections, and paging positions intact.

Editing a Selector inside Preparation is a local interaction, not navigation. It MAY temporarily own focus or open a transient chooser, but it MUST NOT add a technical parameter to the View breadcrumb or navigation stack.

### Default command bindings

The Terminal Host normalizes physical key, character, wheel, and pointer events without assigning workflow meaning. The Interaction Core resolves commands according to the focused target and its current input mode. Outside text editing, the default Deckle bindings are:

| Input | Command | Contract |
|---|---|---|
| Arrow keys | Move focus | Move through the current composition's semantic order. |
| Enter | Activate | Invoke the focused target's declared intent. |
| Space | Toggle selection | Change the focused option in a multi-selection interaction. |
| Backspace | Back | Return by one View. |
| Visible Back control | Back | Behave exactly like Backspace. |
| Escape | Cancel flow | Leave the current interaction or flow for its owning Action Menu. |
| Ctrl+C | Exit | Restore the terminal and leave the launcher. |
| Mouse wheel | Previous or next page | Move paginated content by one non-overlapping page. |
| Home, End | First or last page | Reach the beginning or latest page. |

Page Up and Page Down MAY be equivalent alternate bindings, but neither the interaction design nor its discoverability may depend on those keys being present.

The visible Back control is a Navigation Control. It is not an Action and does not represent work.

A focused text editor consumes characters, Space, Backspace, Delete, Home, End, and horizontal arrows before View commands. Its Global Command Indications reflect editing commands and MUST NOT advertise `Backspace · Back` while Backspace edits text. Escape cancels the local edit without accepting its buffer; after the editor closes, the View bindings apply again.

Every paged Panel is focusable and exposes visible Previous Page and Next Page targets whenever more than one page exists. These targets provide the complete keyboard path on a keyboard without Page Up or Page Down. A wheel event targets the Panel beneath its pointer coordinates when those coordinates are available; otherwise it targets the focused paged Panel. When several Panels overflow, Scrolling Command Indications describe the currently targeted Panel.

If a transient chooser or Confirmation currently owns input, Escape cancels that local interaction first. At the root Action Menu, Cancel flow has no effect.

Global Command Indications MUST be generated as structured key-or-gesture and command-label pairs from the active bindings and placed in the Persistent Header's upper-right track. They are non-interactive legends, not the visible Back control. When color is available, the key or gesture and its command label use two nearby grey levels; the pair remains understandable without color. Command pairs are grouped by spacing rather than a rule between every pair. On a narrow terminal, indications hide by priority before the context is clipped: currently necessary activation or editing commands remain, then cancellation or Back, while already familiar movement indications may collapse first.

Scrolling Command Indications are the only command legends normally placed at the bottom. They appear only when the current View or Panel can scroll or paginate, and they MUST include the mouse wheel whenever wheel paging is available.

A View MUST NOT advertise a command it cannot currently honor.

### Focus and activation

A View with interactive content has exactly one focused target. Focus survives redraw and resize and returns with retained View state. Each Panel may retain local content state through a keyed immutable snapshot, but the Interaction Core owns the session copy and the association between that state and its Panel.

Every interactive target declares its intent. Activation MUST NOT infer intent from an ellipsis, a `Value` naming convention, a color role, or the target's position.

Visual role and interaction intent are independent. A destructive Confirmation can therefore share activation mechanics with an ordinary Confirmation while retaining distinct presentation and safety contracts. Color MUST NOT be the only carrier of focus, checked, disabled, danger, or completion state; every state has a textual or structural distinction.

An Action that cannot run under the active PowerShell engine remains visible. It MUST either be disabled with a concise explanation of its engine requirement or explicitly delegate to a compatible installed engine.

## View composition

### View

A View is the complete visible interface state. It always contains one Persistent Header and one View Body.

Navigation creates another View only when the user enters another durable interface context. Focus changes, Selector editing, validation messages, paging, and responsive reflow do not create Views.

### Persistent Header

The Persistent Header has four stable responsibilities:

1. present the repository-supplied banner;
2. identify the current context;
3. present active Global Command Indications;
4. separate itself from the View Body.

Its structure persists across Views. Its context and command indications derive from current state. Body compositions MUST NOT reproduce the header.

One primary separator closes the Persistent Header. Sections MAY use lighter dashed separators, and whitespace separates groups. The interface MUST NOT draw a full-width rule for every Section or item.

### View Body

The View Body composes semantic regions. It MAY contain an Action Menu, Preparation, and one or more Panels together or separately.

Rows and columns are layout results, never semantic children exposed to workflow callers. Reflow MAY change placement but MUST preserve each composition's responsibility, content, focus target, interaction intent, and semantic order.

The focused target and checked Selections MUST remain clearly distinguishable in every supported color mode.

### Panel

A Panel owns one content responsibility and declares the schema and behavior of its local overflow state. The Interaction Core retains that state by Panel identity for the current session. A Panel MUST remain identifiable when responsive layout moves or resizes it.

Panel titles name their content, not the generic fact that content exists. `Results` is therefore unsuitable when the content is specifically a Report, Execution Journal, Execution Tracking account, or Review.

## Action Menu

An Action Menu MAY contain both Actions and Accesses because its responsibility is to organize the work available from one context.

- A Section is non-interactive and groups related items.
- An Action starts an interaction flow for intended work.
- An Access opens another View.
- An Action Row keeps one Action visible beside its Action Variants.
- An Action Variant changes how the Action is carried out without navigating.

Actions without variants MAY occupy one activation target. Action Rows with variants MUST preserve the Action subject when focus moves among variants.

The semantic order of Sections, Actions, Variants, and Accesses is stable. Responsive layout MAY change widths or placement but MUST NOT reorder them according to available geometry.

The composition grammar reserves two later insertion points without rendering them in the first cycle: a global Logs Access above Quit at the far right of the main Action Menu, and a Rerun Action immediately to the right of the visible Back control in a completed Execution View.

## Preparation

Preparation composes the inputs needed to begin one Action without turning each input into navigation.

Its grammar is:

- a **Filter** is a material criterion that constrains what the Action will affect;
- a **Selector** is the interaction that edits one Filter or another material Action input;
- a **Selection** is the value or values currently accepted by one Selector;
- the **Effective Scope** is the resolved set the Action will actually inspect or change after its target and Filters are applied;
- **Review** is the read-only account of the intended Action, its material inputs, and Effective Scope;
- **Confirmation** is the deliberate activation that permits Execution to begin.

A Preparation MUST keep its Selectors, Effective Scope, Review, and Confirmation in one View Body. It MAY paginate vertically when height requires it; pagination MUST retain every Selection and the focused Selector.

A Selector declares whether it accepts one value, several values, free text, or a bounded edit. Multi-selection is state within one Selector, not a collection of navigable Views.

Effective Scope MUST be derived from current Selections and the selected execution target. A failure to resolve it appears beside the relevant Preparation content and MUST NOT create an error destination.

The Launcher supplies one Preparation Controller for the Action. It owns accepted Selections, creates a monotonically distinct immutable revision after every accepted change, resolves Effective Scope, validates the revision, and publishes its snapshot to the Interaction Core. Every asynchronous resolution is tagged with the revision it started from; a result for a stale revision is discarded.

Every accepted Preparation change invalidates the prior Review. Review and Confirmation reference the same resolved revision identifier. Confirmation is unavailable while resolution or validation is pending or failed, and the Execution Request carries that exact reviewed revision. Beginning Execution from another revision violates the contract. Interaction Compositions render and edit drafts through descriptors; they do not resolve repository scope themselves.

Destructive Confirmation MUST name the effect and Effective Scope, distinguish the safe choice, focus the safe choice initially, and require deliberate activation of the destructive choice. A generic caller option MUST NOT reverse the safe initial focus. Escape cancels it.

## Execution

The Deckle Launcher begins Execution only from an Execution Request. The Execution Runtime freezes that reviewed Action revision for one run. Later edits create another Execution rather than mutating the running or completed one.

The Execution Runtime publishes updates asynchronously with respect to the interaction loop. Focus and Journal paging remain usable while a run is active even though navigation away from the Execution View is unavailable.

The default flow uses `Action Menu → Preparation → Execution`. Choosing an Action pushes Preparation when material input or Confirmation is required. An Execution Request replaces Preparation with Execution so Back after completion returns to the owning Action Menu, not to a stale Review. An immediately executable Action pushes Execution directly over its retained owning Action Menu. An Access remains the only catalog item whose purpose is to push another durable View context.

### Primary Execution flow

The Persistent Header and its primary separator remain. The Action Menu disappears. The View Body presents the visible Back control, followed directly by the Execution View.

While Execution is `Running`, the visible Back control, Backspace, and Escape are unavailable because the first cycle neither backgrounds nor cancels a child process. The Header does not advertise them, and Tracking states that the run must finish before returning. After completion, the visible Back control and Backspace return to the owning Action Menu.

Ctrl+C remains the emergency session exit during a run. The Execution Runtime MUST forward an interrupt through the selected engine or process adapter, wait for the child to exit or reach a declared forced-termination boundary, publish cancellation or failure, and only then allow terminal restoration. It MUST NOT abandon a redirected child process that can continue writing to disposed pipes.

The first cycle renders no Rerun control. The reserved Rerun insertion point does not change the Back control's stable position.

### Execution View

The Execution View presents two independent Panels:

- the Execution Journal Panel presents the detailed emitted evidence;
- the Execution Tracking Panel presents Deckle's concise account of significant steps, current state, and final conclusion.

On a wide terminal, the Journal occupies approximately five-sixths of the usable width and Tracking one-sixth. The renderer uses measured minimum viable widths for both Panels to choose the split; the ratio alone MUST NOT make Tracking unreadable. In a narrow IDE panel, a height-limited Journal appears above Tracking so both remain visible in their established order.

The Execution composition MUST use the terminal's usable width. A global preferred-width cap intended for menus MUST NOT constrain it.

### Execution Journal

The Journal retains structured records in observed admission order. A record carries its observed time, source, stream, complete logical content, and presentation segments. Order is preserved within each captured stream; no exact emission order is promised between independently redirected stdout and stderr streams.

Journal lines MUST remain complete in retained state and MUST NOT wrap in the panel. Rendering clips them to the available display cells without corrupting presentation sequences. Paging changes the visible records, not the retained records.

ANSI preservation uses an explicit allowlist of SGR presentation semantics: foreground and background color, intensity, emphasis, and reset. The parser retains semantic segments rather than raw escape sequences, carries incomplete sequences across reads, and resets presentation at every rendered line boundary. It discards OSC, cursor movement, erasure, title changes, private modes, and any unsupported control instead of replaying them.

A carriage return replaces the current provisional native-progress record until a newline commits it; PowerShell `ProgressRecord` values are a separate input kind and MUST be admitted explicitly if supported. Empty lines are retained. Tabs remain logical tab characters in state and expand to the next configured tab stop only during rendering.

Direct child-process pass-through cannot satisfy this containment contract. The first implementation uses captured output. ConPTY remains an optional host strategy only when a concrete workflow requires genuine terminal behavior.

While an Execution runs, the Journal follows its latest page by default. After completion, a retained Journal reopens on its latest page. Guidance, Review, and Reports open at their beginning.

### Execution Tracking

Tracking is Deckle-owned state, not a classification projected from journal lines. It records significant workflow steps, the current step and state, elapsed time where useful, and the eventual Execution Result.

The executor MUST publish a structured conclusion. Free text such as `Result : Success` MAY be admitted as compatibility evidence but MUST NOT remain the canonical source of the Execution Result.

The result vocabulary supports success, failure, partial completion, skipped work, and cancellation. A first-cycle runner need not offer user cancellation to produce or display a cancellation reported by a workflow.

### Action-owned output

An Execution MAY produce action-owned output in addition to its Journal and Result. That output uses its domain name: statistics produce a Report, builds may produce Artifacts, and other Actions may produce changed files, a plan, or another named deliverable. The execution adapter returns the structured value, the Runtime transports it unchanged in `Completed`, and the Launcher maps it to its Action-owned presentation.

The Interaction Core MUST NOT relabel this output as the Execution Result. Its presentation is supplied by the Action's composition and MAY coexist with or follow the completed Execution View.

The framework MUST NOT invent a continuation that the workflow does not implement. An Action that produces a plan has completed with that plan unless the repository separately declares an Apply or another continuation Action.

## Rendering and responsive behavior

Rendering is a deterministic projection of retained state, terminal metrics, theme, and host capabilities. A complete redraw is valid whenever geometry or capability state changes; continuous resize animation is not a goal.

### Semantic presentation and theme

Semantic descriptors state what an interface object is. The renderer derives a presentation role from that object and its composition, then the active theme maps the role plus interaction state to terminal attributes. Workflow callers MUST NOT supply `ConsoleColor`, ANSI codes, focus colors, or layout-dependent variants.

Presentation and behavior remain independent. An Action Variant is still an Action intent even when it inherits the body color; an Access keeps its Access intent even if another theme gives it the same color as an Action. Focus, disabled, checked, danger, and completion are state overlays rather than replacements for the underlying semantic role.

The default Deckle terminal theme preserves the existing script hierarchy:

| Semantic presentation | Default Deckle treatment |
|---|---|
| Repository banner | Blue |
| Current context | Dark grey |
| Section, Panel, Review, and Effective Scope title | Magenta |
| Section separator | Light grey dashed rule |
| Action subject, Filter label, safe standalone Action, and safe Confirmation | Cyan |
| Action Variant, Selection, Review body, Effective Scope body, and ordinary detail | Inherit the terminal foreground |
| Access, Selector target, and current editable value | Dark yellow |
| Navigation Control and supporting explanation | Dark grey |
| Exit and destructive choice | Red |
| Completed, running or partial, and failed Tracking state | Green, yellow, and red respectively |
| Global or scrolling command key | Grey |
| Global or scrolling command label | A nearby darker grey |

Ordinary focus uses a high-contrast selection background while retaining a structural focus marker. Focused danger and Exit use a distinct danger-focused treatment. Disabled targets remain present, include a concise reason, and use a structural marker plus muted treatment. If color is unavailable, these markers, labels, grouping, and state text preserve the same meaning.

Execution Journal presentation is not remapped through the launcher theme. Admitted native presentation segments retain their own allowed semantics inside the Journal Panel; launcher-owned Panel titles, Tracking states, and Execution Result use the Deckle theme.

Resize MUST preserve:

- current View and navigation stack;
- focused semantic target;
- Selections and confirmed values;
- current Execution state and retained Journal;
- the nearest valid page for each paged Panel.

The renderer MUST remain usable in narrow IDE panels and wide standalone terminals. It MUST use available width, avoid fixed content-width caps, preserve semantic order, and avoid redundant redraws that cause excessive flicker.

The renderer MUST leave the terminal in a valid state after narrow dimensions, interrupted drawing, an exception, or normal exit. When a terminal becomes too small for even the narrow composition, it presents one resize state rather than partially drawing an unusable View.

Layout uses available display cells, not string length alone. Clipping MUST account for presentation sequences and SHOULD account for wide and combining characters before reuse is declared complete.

Paging is the first-cycle overflow interaction. Fine line scrolling MAY be added later without changing Panel content or navigation contracts. Mouse-wheel paging is a required path; Page Up and Page Down are optional equivalents.

## Capability and engine boundaries

The Terminal Host probes capabilities instead of using the PowerShell version, terminal brand, or parent process as a proxy. Every capability reports `Supported`, `Unsupported`, or `Unknown`. At minimum the Host reports:

- interactive input and output availability;
- terminal width and height;
- cursor addressing and clear support;
- color and safe VT presentation support;
- alternate-buffer support;
- pointer-input support.

Capability degradation is explicit:

- without color, structural and textual markers preserve every state;
- without an alternate buffer, the system MAY use the main buffer but MUST restore its cursor and modes without erasing prior content;
- without cursor addressing, the structured interface refuses to start and presents one static compatibility explanation;
- without pointer input, visible page targets keep the interface complete by keyboard;
- every changed input or output mode is restored to its exact observed prior value.

Mouse-wheel paging is required wherever the Windows host reports pointer input as `Supported`; a host that cannot provide it degrades to the complete keyboard path. Click activation is an enhancement.

The launcher and daily Actions target Windows PowerShell 5.1 and PowerShell 7. All bootstrap files loaded by both engines MUST parse and import under Windows PowerShell 5.1. Engine-specific syntax stays behind a file or process boundary that an incompatible parser never reads. Source files loaded by Windows PowerShell 5.1 use an encoding it decodes deterministically, including files containing arrows, ellipses, and accents.

Each Action declares whether it can run under either engine or requires one specific engine. Parser syntax, source encoding, safe argument construction, and platform detection MUST remain outside semantic compositions. The execution adapter constructs arguments through the runtime's supported API and MUST NOT fall back to ambiguous string concatenation.

An execution adapter distinguishes engine choice, shell profile behavior, working directory, and elevation. None of those concerns may be inferred from another.

## Reuse contract

The reusable modules MUST accept repository-owned branding, contexts, Actions, labels, engine requirements, and execution adapters as inputs. They MUST NOT contain:

- Deckle paths, branches, configurations, or command names;
- assumptions about worktrees, releases, statistics, or Folder Covers;
- hard-coded output labels such as `Results`;
- direct calls to repository workflow scripts.

One repository adopting the framework should need to inject a launcher context containing its branding, catalog, Action handlers, execution adapters, Tracking steps, and action-owned outputs. It should not need to edit a reusable module's internal files or depend on launcher globals. Exit and other control flow cross the facade as public transitions rather than internal exception types.

The first cycle proves reuse through a repository-neutral configuration fixture and the absence of Deckle assumptions from the public surface. Adoption by a later consumer provides the cross-repository integration proof. The first cycle does not provide a shared package, installer, or update channel.

## Conformance scenarios

The first implementation is conformant when all of these scenarios hold through the public surface:

1. **Mixed menu** — one Action Menu presents Sections, an Action Row with variants, standalone Actions, and Accesses; activation follows declared intent rather than presentation.
2. **Compact Preparation** — a statistics Action edits scope, files, measures, grouping, thresholds, and target in one Preparation; Review and Confirmation use the same resolved Effective Scope.
3. **Execution separation** — a running build replaces the Action Menu, updates Journal and Tracking independently, preserves safe native presentation, and cannot draw across the Tracking Panel.
4. **Navigation contract** — visible Back and Backspace produce the same one-View transition; Escape cancels the current flow to its owning Action Menu; Ctrl+C restores and exits the terminal session.
5. **Discoverable input** — arrow, Enter, Space, Backspace, Escape, and wheel commands are usable and shown in the correct indication region when active.
6. **Retained state** — resizing and paging retain focus, Selections, Execution state, and complete Journal records in both narrow and wide layouts.
7. **Content policy** — a completed Journal opens at its latest page while a Report opens at its beginning.
8. **Engine boundary** — a 5.1-only, 7-only, and either-engine Action each presents and dispatches according to its declared requirement without changing the composition.
9. **Text editing** — a focused free-text Selector consumes Space, Backspace, Delete, Home, End, and horizontal arrows without triggering View commands.
10. **Multiple overflow regions** — wheel and keyboard paging target the deterministic active Panel, and every page remains reachable without pointer input or Page Up and Page Down keys.
11. **Running navigation** — Back, Backspace, and Escape remain unavailable while an Execution is running; emergency Ctrl+C quiesces the child before the terminal session closes.
12. **ANSI containment** — fragmented and nested SGR is preserved semantically, hostile OSC and cursor controls are discarded, and presentation resets at every rendered line boundary.
13. **Captured order and progress** — simultaneous stdout and stderr preserve per-stream order without claiming exact cross-stream emission order; carriage-return progress updates one provisional record.
14. **Degraded hosts** — narrow headers, no-color output, no alternate buffer, absent pointer input, and absent cursor addressing follow their declared degradation policy.
15. **Bootstrap compatibility** — the launcher parses and imports under Windows PowerShell 5.1 before dispatching 5.1-only, 7-only, and either-engine Actions.
16. **Repository-neutral fixture** — different branding and workflows are declared without changing a reusable module's internal files or installing a shared package.

Tests SHOULD assert these behavioral contracts through the values and public seams the implementation actually uses. Renderer-unit tests MAY additionally assert pure layout, clipping, color, and separator invariants.

Host acceptance MUST exercise Windows Terminal, conhost, and the supported IDE terminal under both Windows PowerShell 5.1 and PowerShell 7. These named environments form the validation matrix; runtime behavior still follows probed capabilities rather than host names.
