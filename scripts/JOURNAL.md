---
description: Dated findings about Deckle's repository-script interaction system.
type: module-journal
---

# Journal — Deckle Scripts

Durable findings about the script launcher and its terminal interaction model. Most recent on top.

## 2026-08-12 — Parallel interaction preview

Chose `scripts/deckle-preview.ps1` as a second, preview-only launcher while `scripts/deckle.ps1` remains the daily reference. The preview mirrors the Action Menu and Execution compositions but invokes no repository command; every Action opens an in-memory sample Execution.

Chose deterministic snapshot rendering as a test seam. The same semantic descriptors render the root menu and Execution at declared widths and heights without taking over the terminal, including wide side-by-side Panels, narrow stacked Panels, long-line clipping, and height-driven paging.

Found that the preview module, native console bridge, behavioral tests, and hidden-console render smoke run under both PowerShell 7.6.4 and Windows PowerShell 5.1 on the current Windows host. This does not replace validation across Windows Terminal, conhost, and IDE terminals.

Found that `continue` inside a PowerShell `switch` nested in a candidate loop continues the `switch`, not the outer loop. Directional navigation must reject an ineligible candidate before it reaches scoring; the regression is recorded beside the terminal-interaction navigation tests.

## 2026-08-12 — Interaction chrome, input, and portability

Chose global command indications as non-interactive key-to-command legends in the Persistent Header's upper-right region. They describe stable controls such as navigation, activation, and return; they are distinct from the explicit selectable Back control in the View Body. Keys or gestures and their command labels use two nearby grey levels rather than a separator between every pair.

Chose scrolling command indications as the only command legend normally placed at the bottom of a view, and only when that view can scroll or paginate. The canonical terms are **global command indications** and **scrolling command indications**.

Chose Back as an explicit Navigation Control with a stable position in nested views. The visible Back control and Backspace invoke the same command: return by one View. Escape instead cancels the current interaction or flow and returns to its owning Action Menu; Ctrl+C exits the launcher.

Reserved the position immediately to the right of the Back control for a future reusable Rerun Action, applicable to operations such as rebuilding or rescanning. Reserved Logs as a future global Access above Quit at the far right of the main Action Menu. The first cycle keeps an Execution only in memory and does not require Rerun, durable run identity, or history.

For the primary execution flow, chose to retain the Persistent Header and its separator while replacing the Action Menu with Back and the Execution View moved directly beneath it. A View Body may still compose an Action Menu, Preparation, and Panels when another workflow genuinely needs them together.

Chose a compact visual hierarchy: one primary rule beneath the Persistent Header, lighter dashed rules for Sections, and whitespace between groups. Avoid a full-width rule for every section or item. Preserve the existing strong focus and checked-state contrast.

Chose keyboard-first interaction with visible discoverability for arrow navigation, Enter activation, Space selection, Backspace return, and Escape cancellation. Mouse click is an enhancement; mouse-wheel scrolling is a requirement because the maintainer's keyboard has no Page Up or Page Down keys.

Chose responsive correctness over continuous resize animation. An occasional clean redraw is acceptable; fixed content width, semantic reordering, excessive flicker, and elaborate work solely for live resize are not. The renderer must remain usable in narrow IDE panels and wide standalone terminals.

Chose Windows PowerShell 5.1 and PowerShell 7 as the compatibility target for the launcher and daily actions. Engine requirements are declared per action; an incompatible action remains visible but disabled with an explanation, or may delegate to `pwsh` when installed. Terminal capabilities are detected independently from the PowerShell engine.

Chose a reusable interaction framework whose repositories declare Sections, Accesses, Actions, Action Variants, Preparation fields, and command handlers separately. Deckle is the first application and Folder Covers is a later consumer; shared cross-repository installation and automatic updating are explicitly deferred.

Chose a reusable architecture with the Deckle Launcher coordinating two one-way branches: Interaction Compositions through the Interaction Core and Interaction Renderer to the Terminal Host, and the Execution Runtime to workflow commands. Workflow commands remain independent CLI entry points, and renderer shapes are internal rather than caller contracts.

The reported request for “root” during launch is not yet explained. Treat elevation, working-directory selection, and shell-profile behavior as separate hypotheses until the prompt is reproduced and traced.

## 2026-08-12 — Action output fidelity

Chose to separate action feedback into a narrow launcher-owned recap and a wide detailed-output region. The recap keeps stable workflow stages and the final success, failure, and error state; the detailed region preserves the invoked command's output as faithfully as possible.

Chose native output fidelity as a requirement for the detailed region, including the command's own color semantics. Capturing and repainting every line must not silently replace those semantics; the implementation technique remains open.

Chose the detailed execution journal as the wide left column and execution tracking as the narrow right column, approximately five-sixths and one-sixth of the available width on wide terminals. Journal lines do not wrap: the complete line is retained while only the available prefix is rendered.

Found that direct child-process pass-through cannot preserve a stable right-hand tracking column: the child owns the full terminal width and can write across that region. A two-column execution surface therefore requires controlled rendering or a separately hosted terminal; the first prototype will keep captured lines and preserve their ANSI presentation rather than start with ConPTY.

Chose action preparation as one compact, low-depth filter form rather than one destination per parameter. It exposes only filters that materially affect the action, keeps the resolved scope and review in the same view, and paginates the form only when the terminal height requires it.

Chose the action menu as a stable column grid for Sections, Accesses, Action Rows, and Action Variants. Its widths remain responsive, but resizing does not change the semantic role or order of its tracks.

## 2026-08-12 — Terminal interaction audit

Found that the reusable menu engine models presentation shapes (`Prefix`, `Cells`, `TrailingCell`) rather than user-facing concepts. The same shape currently represents action variants, action targets, independent actions, and destinations depending on the screen, so it cannot yet carry one stable vocabulary across repositories.

Found that generic actions retain their full command grid while running and append the transcript below it. Maintenance reports already implement the intended compact state: the action menu is replaced by Back and a result viewport.

Found that action output is already collected as structured records, including timestamp, level, source, stream, message, and presentation segments. Launcher callers retain only the latest display title and lines, so there is no run identity, history, Logs destination, or retry source despite the richer collector.

Found that the launcher caps content at 74 columns and recreates a cleared viewport when terminal geometry changes. This explains its unused wide space and makes resize flicker structurally possible.

Found that Windows PowerShell 5.1 compatibility is blocked centrally rather than throughout the script tree. After decoding source explicitly as UTF-8, its parser reports PowerShell 7 syntax in four of the 92 PowerShell files under `scripts/`; the launcher also evaluates `$IsWindows` under strict mode, while its BOM-less non-ASCII source is decoded through the legacy code page by Windows PowerShell. Terminal capabilities such as VT output, pointer input, and resize events remain separate from the PowerShell engine version and need their own probes.

Found in the delivered Folder Covers comparison that focus, checked state, keyboard navigation, click toggles, paging, and responsive reflow already compose successfully as reusable interaction mechanics. Its two image workflows stop after writing a JSON plan marked `planned`; there is no apply, download, or generation command, so inventory refresh is not a continuation of that workflow.
