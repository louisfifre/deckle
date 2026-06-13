---
description: Dated decisions and findings for Deckle.Input.Autocorrect — founding choices, measurements, open direction.
type: module-journal
---

# JOURNAL — Deckle.Input.Autocorrect

## 2026-06-12 — Founding architecture

Observation fork closed: Raw Input + targeted UIA reads, repair via SendInput after the word commits,
Enter = a pure reset (no commit, no learning — a send context may already have dispatched the text).
The low-level hook was rejected (permanent system-wide latency, silent removal after timeouts — the
PowerToys Quick Accent failure class); a TSF text service is in-proc COM C++, out of reach for a
managed v1. Accepted cost: the word right before an Enter stays uncorrected.

## 2026-06-13 — Offline eval set the design axis

On a Wikipedia FR eval split, doing nothing scores 84.5 % word accuracy. The design axis is the
false-correction-vs-recall trade, and the context margin is the lever that kills wrong-variant picks.
(The shipped operating-point numbers predate the French-first pivot below, which dropped the English
guard.)

## 2026-06-13 — Test seam and first clean live run

Test seam extracted: three OS ports — `IKeyboardInputHost`, `ISurfaceProber`, `ITextInjector` — let
the real engine run against faked OS (~34 tests: orchestration, surface gates, conditional learning,
suppression, revert). Live on the fixed binary (Notepad): `ecole→école`, `francais→français`,
`vanite→vanité` land clean; diff-minimal injection and the `hDevice==0` self-filter confirmed.
Unresolved live risks left for later: stale surface read before inject, UIA probe on the input pump,
silent UIPI into elevated windows, keystroke-in-flight corruption, self-filter blind to other
synthetic input, double-inject from two run instances.

## 2026-06-13 — Open direction (exploring, nothing built)

French-first: English dropped, French-only, spelling language chosen manually. Recall ambition raised
without loosening the bar — more coverage through richer data and sentence left-context, not a looser
threshold. Corpus toward dictionary-grade coverage (Morphalou for forms, Lexique for frequency,
Wikipedia FR for context). Context to use the sentence's left context up to the last full stop, via a
model still under exploration. Rewrite (generative regeneration, keyboard or dictation) is a separate,
later chantier.
