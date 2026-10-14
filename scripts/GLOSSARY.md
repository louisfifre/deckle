---
description: "Deckle Scripts interaction vocabulary. It names the launcher's choices, action runs, their detailed output, and how they end."
---

# Deckle Scripts, Glossary

Shared language for the reusable terminal launcher. These terms separate navigation from work, and the lifetime of one action run from what it emits and how it ends. Behavioral and composition contracts live in the [terminal interaction specification](TERMINAL-INTERACTION-SPEC.md).

## View anatomy

**View** :
The complete visible state of the Deckle terminal interface. It contains the Persistent Header and the View Body. Navigation opens another View, even when the structure of the header stays the same.
_Avoid_ : page (reserved for pagination), screen (the terminal or a captured image).

**Persistent Header** :
The stable region at the top of every View, made of the banner, the context line, the global command indications and the primary separator. Its structure stays while its content follows the current View.

**Global Command Indications** :
The non-interactive legends in the Persistent Header that pair a key or gesture with a command. They describe the commands available across the current View.
_Avoid_ : action (selectable work), Back control (a selectable way to invoke navigation).

**Scrolling Command Indications** :
The non-interactive legends shown at the bottom, only when the current View or Panel can scroll or paginate.
_Avoid_ : global command indications (remain in the Persistent Header).

**View Body** :
The variable region below the Persistent Header. It may hold an Action Menu, a Preparation and one or more Panels, alone or together.
_Avoid_ : view (also includes the Persistent Header).

**Presentation Role** :
The role an interface object plays in appearance, named by its meaning. The active theme maps it to terminal attributes, together with the current state and the capabilities of the host. It does not decide interaction intent or layout.
_Avoid_ : color (one theme's terminal attribute), intent kind (the behavior activation requests).

**Panel** :
A region of the View Body that presents one distinct content responsibility. Responsive layout may move a Panel without changing what it is. Rows and columns describe only its placement.

**Execution View** :
A View whose body presents one Execution through an Execution Journal Panel and an Execution Tracking Panel.

**Execution Journal Panel** :
The Panel that presents the Execution Journal.

**Execution Tracking Panel** :
The Panel that presents the Execution Tracking.

**Page** :
One slice of a paginated list, shown one at a time. The whole visible state of the interface is a View.

**Screen** :
The terminal itself, or a captured image of it. The visible state of the interface is a View.

**Back control** :
The visible Navigation Control that goes back. It is selectable, unlike a command indication.

**Color** :
One terminal attribute that a theme may assign. A Presentation Role names the meaning, and the theme chooses the color.

**Intent kind** :
The kind of behavior that activating an object requests. It belongs to interaction, while a Presentation Role belongs to appearance.

## Navigation and work

**Section** :
A non-interactive grouping of related Action Menu items.
_Avoid_ : action row (presents one Action and its variants), panel (owns a content responsibility).

**Navigation Control** :
A selectable item that invokes a navigation command, such as the visible Back control.
_Avoid_ : action (chooses work to carry out), access (opens another View).

**Access** :
A selectable item that opens another View, rather than choosing work to carry out. It names where a navigation starts. The View it opens is the destination of that transition.
_Avoid_ : destination (the View's role in the transition), action (chooses work to carry out).

**Action** :
A selectable operation the user intends to carry out. Choosing it starts that operation's flow, which may need Preparation, Review or Confirmation before an Execution begins. It is classified by the intended work, not by those steps of the interface.
_Avoid_ : access (opens another interface state without choosing an operation), execution (one actual run of the chosen operation).

**Action Row** :
A menu composition that presents one Action and its choices together across the layout. It keeps the operation visible while the user chooses how to carry it out.
_Avoid_ : action (the intended operation itself), section (groups several menu items).

**Action Variant** :
One selectable way to carry out the same Action, such as Release or Debug. It changes how the operation is carried out or configured, not what it intends.
_Avoid_ : action (the shared operation), access (opens another navigable state).

**Action Menu** :
A View Body composition that arranges Sections, Accesses, Action Rows and Action Variants in stable tracks grouped by meaning. It may sit beside a Preparation or Panels.

**Preparation** :
A View Body composition in which the user configures an intended Action before its Execution. It gathers what is needed to determine and review the Effective Scope of that Action.

**Filter** :
A material criterion that limits what an Action will affect.
_Avoid_ : selector (the interaction used to edit an input), selection (the accepted value or values).

**Selector** :
An interaction that edits one Filter, or another material input of an Action, during Preparation.
_Avoid_ : selection (the value or values it currently holds), access (opens another View).

**Selection** :
The Options that one Selector currently accepts.
_Avoid_ : selector (the interaction that edits it), focus (the currently active interface target).

**Option** :
One choice that a Selector offers.
_Avoid_ : selection (the currently accepted Option or Options), target (the interactive surface that presents it).

**Radio Group** :
A Selector that accepts a single Option, made of mutually exclusive Radio Buttons.

**Radio Button** :
A Target that presents one Option inside a Radio Group.
_Avoid_ : checkbox (represents an independently includable Option).

**Checkbox Group** :
A Selector that accepts several Options, made of Checkboxes that can each be included on their own.

**Checkbox** :
A Target that includes or excludes one Option from a Checkbox Group.
_Avoid_ : radio button (belongs to a mutually exclusive Selection).

**Toggle Button** :
A Selector that switches one independent setting between two states, rather than choosing membership in a group.
_Avoid_ : checkbox (represents one Option within a multiple Selection).

**Effective Scope** :
The resolved set that an Action will actually inspect or change, once its target and Filters are applied.
_Avoid_ : selection (the Options one Selector accepts), review (the account presented before Execution).

**Review** :
The read-only account of an intended Action, its material inputs and its Effective Scope, before Execution.
_Avoid_ : preparation (the whole configuration composition), confirmation (the acceptance that permits Execution).

**Confirmation** :
The deliberate acceptance of a reviewed Action and its Effective Scope that lets Execution begin.
_Avoid_ : review (presents what will happen), execution (the run that follows acceptance).

**Destination** :
The View a navigation arrives at. It is a role of that View, while the item that leads there is an Access.

**Focus** :
The interface target that is active at this moment. It is where input goes, not the Options a Selector holds.

**Target** :
An interactive surface the user can select, such as a Radio Button or a Checkbox. It presents an Option without being the Option.

## One action run

**Execution** :
One occurrence of an Action, from its start to its completion. It owns the Execution Tracking, the Execution Journal and the eventual Execution Result.
_Avoid_ : execution result (only the final conclusion), execution journal (only the emitted detail).

**Execution Tracking** :
The concise account of an Execution that Deckle maintains, with its significant steps, its current state and its eventual Execution Result. It is kept apart on purpose from the detailed output of the invoked command.
_Avoid_ : execution journal (the command-owned output), execution result (only the final conclusion).

**Execution Journal** :
The detailed output emitted by the scripts and processes involved in one Execution, with their native lines and presentation semantics. It is evidence from the run, not Deckle's concise account of it.
_Avoid_ : execution tracking (Deckle's maintained account), execution result (the final conclusion).

**Execution Result** :
The final conclusion of a completed Execution, such as success, failure, partial completion or cancellation, with the little information needed to understand it. It belongs to the Execution Tracking and does not stand for the whole output of the run.
_Avoid_ : execution (the whole occurrence), execution journal (the detailed emitted output).
