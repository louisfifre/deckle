---
description: A degraded state the person can fix is declared in a registry and shown as tray state; a Windows notification is kept for the moments Deckle asks the person for an action.
---

# ADR-0006 — Signal user-fixable incidents as tray state, never as a notification

**Status** — accepted 2026-10-23

## Context

Deckle keeps running when one of its modules degrades: precision scrolling falls back to the native wheel, the microphone cannot open, a speech model is missing, the Anytype backend stops, the Hue bridge is unreachable, Windows drops toasts because Deckle runs elevated. Each module already writes the opening and the closing of such a state in the log, and the log is read after the fact, often from another machine. Nothing told the person, while it lasted, that a gesture of theirs would restore the module.

The log also carries one-off `Error` entries, a developer's record of something that failed once. Alerting on them would show the person failures they cannot act on, and would make the alert a mirror of the log rather than a state of the app.

Deckle's first use is dictation. A surface that takes the focus while the person dictates breaks the very thing Deckle does. Windows toasts also stop showing at all when the process runs elevated, which is one of the states to signal.

## Decision

A user incident is a degraded state of one module that a gesture of the person ends. Modules MUST declare it by an id in a registry owned by `Deckle.Diagnostics`, opened and closed beside the log lines the module already writes for the degradation and its recovery. The registry MUST write no log line of its own: the incident is a state, the log keeps the record.

The App MUST listen to the registry and show every open incident as a dot on the tray icon and as one line per module at the top of the left-click menu, worded as a fixed word then the module's name. The line's click MUST open Settings on that module's page. The dot MUST stay for as long as an incident is open and go away when the last one closes.

A Windows notification MUST be reserved for the moments Deckle asks the person for an action. It MUST NOT be used to signal a state, an error or a completion.

One-off `Error` entries and developer bugs are shown nowhere and are read in the log. A developer-facing view may come once the person-facing surface is settled.

## Consequences

Every module that gains a user-fixable state adds a declaration and two calls beside its existing lines, and a module that forgets them is silent on the tray; the registry's declared ids are checked by a test so that none is undeclared.

The tray icon becomes a set of files, one per combination of recording state and incident dot, that the maintainer draws and replaces. A module without a Settings page of its own lands on the General page.

An incident with no in-process closing, such as a model missing at boot or toasts dropped under elevation, keeps its dot until Deckle restarts; the gesture that fixes it is a restart anyway.

The person is never notified of a one-off error. What that costs is accepted: the log is the record, and the tray is the state.

## Options considered

- **A. A Windows notification per `Error` incident** — the first lean of the review. It steals the focus during dictation, says nothing while Deckle runs elevated, and reports events the person cannot fix.
- **B. Derive the state from the `Warning` and `Error` lines** — no new calls in the modules, but the log does not know which degradation a gesture ends, nor when it ended; the state would be a guess over a record.
- **C. A registry declared by the module, read by the App (retained)** — one call to add in each module beside its lines, and the App owns the surface.
