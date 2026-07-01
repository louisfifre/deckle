# Deckle — Settings: controls and behaviour

A companion to the deckle-settings-ux skill: the control catalogue and the finer behaviour the skill states only in principle. It names value-natures and gestures, not a UI framework's classes.

## The control catalogue

By the value's nature:
- on/off → a switch; its state shown on the control itself (a shape legible in black-and-white), its label fixed and naming what it governs.
- one of a few mutually exclusive options → radio buttons, every option laid flat and visible; more than a few → a dropdown.
- a numeric magnitude → one paired control (see below).
- a relationship or shape — a curve, an envelope → a dedicated editor, never a row of raw numbers.
- free text → one shared input; multi-line only when the value wraps.
- a path or folder → one normalized picker (see below).
- the fine configuration of something activatable → an inline fold revealed only when it is on.
- a heavy multi-step action → its own navigated sub-page, returnable from the title bar, never a dropdown or a modal.

## Magnitude — one control, a chosen fineness

Always both a slider and a number field, the field being the readout too — never one without the other, so "both" adds nothing to hide.

A linear magnitude is declared by min, max, unit, and a desired fineness (three levels, from coarse to fine) — nothing else is hand-numbered. The fineness sets how many steps should span the range (an order of ten, a hundred, a thousand). The step grain is the value on the 1-2-5 ladder (…, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, …) nearest — as a ratio, not an absolute gap — to (span ÷ target). From that grain the rest follows: the readout shows only the digits the grain implies; the slider steps by the grain; a fine nudge moves one grain, a coarse one ten; the field takes any multiple of the grain in range. Powers of ten are just the ladder's round rungs, not the whole ladder.

A geometric magnitude — one whose useful steps multiply rather than add (memory sizes, frequencies: 1, 2, 4, … 256) — is not linear: it is declared by its list of stops. The slider indexes the list (snapping to a stop), the field shows the formatted value. The grain rule above does not apply.

## Path — a picker, its affordances by ownership

One normalized picker, its affordances set by who owns the location and how the user reaches it:
- read-only, with Change + Open: the user repoints by browsing.
- typeable, with Change + Open: the faster route when the user already knows the path and pastes it (a models folder carried from another machine) rather than browsing to it.
- open-only, a way in but no Change: a location the app owns and should not move.

## Grouping — a fold, with or without a master

- a fold governed by a master switch: its settings are revealed when the master is on, hidden when off (masked, never greyed). Use it when one switch commands everything inside. (We call it a group.)
- a fold that only collects related settings, no master: each stands on its own condition. Use it when there is no single on/off to command the whole. (A section.)

## Reset — three levels, one default

Offered at three nested levels, each appearing on hover and active only once something has changed: a single value, its group or section, the whole surface. The default has one source: the same declaration feeds both what the reset writes and the "has it changed?" test — never two copies that could disagree. When the default is a runtime resolution (the system device, an auto-path), that source is the sentinel meaning "resolve at runtime", not the resolved value; the reset rewrites the sentinel, and change is tested against it.

A single-value reset is instant. A group or surface reset writes many values at once — a group reset returns its master too, which may switch the group off and hide its settings — so it is confirmed before it acts.

## Confirmation — on irreversibility

Set by how reversible the action is, never by where the setting sits. A single-value edit or reset is trivially reversible — no gate. An action that wipes many values (group or surface reset), clears a pairing, deletes on disk, or overwrites live state is confirmed first.

The gate is copy-agnostic: the caller supplies the wording (title, body, verb); the service owns only the shared Cancel. When the action is destructive, the safe button is the default — Enter commits nothing, the user reaches for the verb on purpose.
