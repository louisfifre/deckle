---
description: "Deckle Scripts interaction vocabulary — launcher choices, action runs, detailed output, and completion."
type: agent-instructions
---

# Deckle Scripts — Context

Shared language for the reusable terminal launcher. These terms separate navigation from work and the lifetime of one action run from what it emits and how it ends. Behavioral and composition contracts live in the [terminal interaction specification](TERMINAL-INTERACTION-SPEC.md).

## View anatomy

**View**:
The complete visible state of the Deckle terminal interface. It contains the Persistent Header and the View Body; navigation opens another View even when the header's structure remains stable.
_Avoid_: page (reserved for pagination), screen (the terminal or a captured image).

**Persistent Header**:
The stable top region of every View: banner, context line, global command indications, and primary separator. Its structure persists while its content reflects the current View.

**Global Command Indications**:
The non-interactive key-or-gesture to command legends in the Persistent Header. They describe commands available across the current View.
_Avoid_: action (selectable work), Back control (a selectable way to invoke navigation).

**Scrolling Command Indications**:
The non-interactive legends shown at the bottom only when the current View or Panel can scroll or paginate.
_Avoid_: global command indications (remain in the Persistent Header).

**View Body**:
The variable region below the Persistent Header. It may compose an Action Menu, Preparation, and one or more Panels, separately or together.
_Avoid_: view (also includes the Persistent Header).

**Presentation Role**:
The semantic visual responsibility projected from an interface object. The active theme maps it together with current state and host capabilities to terminal attributes; it does not determine interaction intent or layout.
_Avoid_: color (one theme's terminal attribute), intent kind (the behavior activation requests).

**Panel**:
A semantic region within the View Body that presents a distinct content responsibility. Responsive layout may move a Panel without changing what it is; rows and columns describe only its placement.

**Execution View**:
A View whose body presents one Execution through an Execution Journal Panel and an Execution Tracking Panel.

**Execution Journal Panel**:
The Panel responsible for presenting the Execution Journal.

**Execution Tracking Panel**:
The Panel responsible for presenting Execution Tracking.

## Navigation and work

**Section**:
A non-interactive grouping of related Action Menu items.
_Avoid_: action row (presents one Action and its variants), panel (owns a content responsibility).

**Navigation Control**:
A selectable interface target that invokes a navigation command, such as the visible Back control.
_Avoid_: action (chooses work to carry out), access (opens another View).

**Access**:
A selectable item whose purpose is to open another View rather than choose work to carry out. It names the origin of navigation; the opened View is the destination of that transition.
_Avoid_: destination (the View's role in the transition), action (chooses work to carry out).

**Action**:
A selectable operation the user intends to carry out. Choosing it starts that operation's interaction flow, which may require Preparation, Review, or Confirmation before an Execution begins; it remains classified by the intended work rather than by those interface transitions.
_Avoid_: access (opens another interface state without choosing an operation), execution (one actual run of the chosen operation).

**Action Row**:
The menu composition that presents one Action and its selectable possibilities together across the layout. It keeps the operation visible while the user chooses how it should be carried out.
_Avoid_: action (the intended operation itself), section (groups several menu items).

**Action Variant**:
One selectable way to carry out the same Action, such as Release or Debug. It changes the manner or configuration of the operation without changing its intention.
_Avoid_: action (the shared operation), access (opens another navigable state).

**Action Menu**:
A View Body composition that organizes Sections, Accesses, Action Rows, and Action Variants in stable semantic tracks. It may coexist with Preparation or Panels.

**Preparation**:
A View Body composition in which the user configures an intended Action before its Execution. It brings together the information needed to determine and review the Effective Scope of that Action.

**Filter**:
A material criterion that constrains what an Action will affect.
_Avoid_: selector (the interaction used to edit an input), selection (the accepted value or values).

**Selector**:
The interaction used to edit one Filter or another material Action input during Preparation.
_Avoid_: selection (the value or values it currently holds), access (opens another View).

**Selection**:
The value or values currently accepted by one Selector.
_Avoid_: selector (the interaction that edits it), focus (the currently active interface target).

**Effective Scope**:
The resolved set an Action will actually inspect or change after its target and Filters are applied.
_Avoid_: selection (one accepted input), review (the account presented before Execution).

**Review**:
The read-only account of an intended Action, its material inputs, and its Effective Scope before Execution.
_Avoid_: preparation (the whole configuration composition), confirmation (the acceptance that permits Execution).

**Confirmation**:
The deliberate acceptance of a reviewed Action and its Effective Scope that permits Execution to begin.
_Avoid_: review (presents what will happen), execution (the run that follows acceptance).

## One action run

**Execution**:
One occurrence of an Action, from its start through its completion. It owns the Execution Tracking, Execution Journal, and eventual Execution Result.
_Avoid_: execution result (only the final conclusion), execution journal (only the emitted detail).

**Execution Tracking**:
The concise Deckle-owned account of an Execution: its significant steps, current state, and eventual Execution Result. It is intentionally separate from the invoked command's detailed output.
_Avoid_: execution journal (the command-owned output), execution result (only the final conclusion).

**Execution Journal**:
The detailed output emitted by the scripts and processes involved in one Execution, including their native lines and presentation semantics. It is evidence from the run, not Deckle's concise account of it.
_Avoid_: execution tracking (Deckle's maintained account), execution result (the final conclusion).

**Execution Result**:
The final conclusion of a completed Execution, such as success, failure, partial completion, or cancellation, plus the small amount of information needed to understand that conclusion. It belongs to Execution Tracking rather than representing the total output of the run.
_Avoid_: execution (the whole occurrence), execution journal (the detailed emitted output).
