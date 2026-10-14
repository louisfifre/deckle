---
description: the control for each nature of value and the finer magnitude, path, fold, reset and confirmation rules, when choosing a control or wiring a reset
---

The annex of the deckle-settings-ux skill. It names natures of value and gestures, not a UI framework's classes.

## The control catalogue

The nature of the value picks the control.

- An on/off value takes a switch. Its state shows on the control itself, in a shape legible in black and white, and its label stays fixed and names what it governs.
- One of a few mutually exclusive options takes radio buttons, every option laid flat and visible. More than a few options take a dropdown.
- A numeric magnitude takes one control, a field with an optional slider, as the magnitude section says.
- A relationship or a shape, a curve, an envelope, takes its values as fields, with a drawing that shows them, never the drawing alone.
- Free text takes one shared input, multi-line only when the value wraps.
- A path or a folder takes one normalized picker, as the path section says.
- The fine configuration of something activatable takes an inline fold revealed only when it is on.
- A heavy multi-step action takes its own page in the rail, reached from a card and left through the breadcrumb, never a dropdown or a modal.

## Magnitude, one control with a chosen fineness

One control, a number field that is also the readout, with a slider beside it when the value earns a gesture. A figure that is typed and never swept, a count or a duration nobody drags, keeps the field alone. It is the same control without its slider, not a second one. A count is declared as such, so its grain never drops below one.

A linear magnitude is declared by its minimum, its maximum, its unit and a desired fineness, three levels from coarse to fine. Nothing else is hand-numbered. The fineness sets how many steps should span the range, an order of ten, a hundred or a thousand. The step grain is the value on the 1-2-5 ladder (0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10 and so on) nearest, as a ratio, to the span divided by that target. From that grain the rest follows. The readout shows only the digits the grain implies. The slider steps by the grain. A fine nudge moves one grain and a coarse one ten. The field takes any multiple of the grain in range. Powers of ten are the ladder's round rungs, not the whole ladder.

A geometric magnitude is one whose useful steps multiply rather than add, such as memory sizes or frequencies (1, 2, 4, up to 256). It is declared by its list of stops. The slider indexes the list and snaps to a stop, and the field shows the formatted value. The grain rule above does not apply.

## Path, a picker whose affordances follow ownership

One normalized picker. Its affordances follow who owns the location and how the user reaches it.

- Read-only, with Change and Open. The user repoints by browsing.
- Typeable, with Change and Open. The faster route when the user already knows the path and pastes it, such as a models folder carried from another machine.
- Open-only, a way in but no Change. A location the app owns and should not move.

## Grouping, a fold with or without a master

- A fold governed by a master switch. Its settings are revealed when the master is on and hidden when it is off. Use it when one switch commands everything inside. We call it a group.
- A fold that only collects related settings, with no master. Each setting stands on its own condition. Use it when no single switch commands the whole.
- A fold whose header carries a choice. Its cards change with the choice, each shown under the value it belongs to, and a note stands in when the chosen value has nothing to set. Use it when a choice decides which settings exist. We call it a mode.

## Reset, the finer rules

The same declaration feeds what the reset writes and the test that says whether the value changed, so the two can never disagree. When the default is resolved at runtime, such as the system device or an automatic path, the source is the sentinel that means "resolve at runtime", not the resolved value. The reset rewrites the sentinel, and change is tested against it.

A single-value reset is instant. A group or surface reset writes many values at once, and a group reset returns its master too, which may switch the group off and hide its settings. So it is confirmed before it acts.

## Confirmation, the finer rules

A single-value edit or reset is trivially reversible and has no gate. An action that wipes many values, clears a pairing, deletes on disk or overwrites live state is confirmed first.

The gate owns no wording. The caller supplies the title, the body and the verb, and the service owns only the shared Cancel. When the action is destructive, the safe button is the default. Enter commits nothing, and the user reaches for the verb on purpose.
