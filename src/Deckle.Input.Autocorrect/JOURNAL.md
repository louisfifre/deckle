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

## 2026-06-13 — Reranker tuned and measured on real dictation

Built an eval corpus from the app's own transcription telemetry (~208k words, the `corpus/raw`
tiers under `%LOCALAPPDATA%\Deckle\telemetry`) — the real distribution, French dense with English
tech terms. Reranker measured on a 25k slice (the full corpus is ~40 min: one MLM pass per ambiguous
slot). Operating point gate + proper-noun caps guard + CamemBERT reranker (freq-prior 1, margin 2):
precision 99.38 %, recall 69 %, reranker stage 784/786; gate-only baseline 98.4 % / 41.5 %. The
frequency prior (fill-mask logit + log-frequency) keeps the common form unless the model strongly
overrules it; the caps guard leaves a title-cased mid-utterance word alone (proper noun). Recall is
ceilinged by the reranker abstaining on `à`/`là` at margin 2 — lowering the margin trades precision.

Two findings. The ASR ground-truth in telemetry writes « çà » for « ça » — a reference artifact that
scores a correct engine as wrong; clean it before using telemetry as an eval reference. On real text
the gate's false corrections split cleanly: capitalized proper nouns (`Git` 64×, killed by the caps
guard) and verb conjugations absent from Lexique (`captes`, `renommes` — the coverage gap Morphalou
would close). The offline `dry-run` command (gate + reranker verdict on text as typed, no injection)
is the iteration tool; the live observation mode is still unbuilt.

## 2026-06-13 — Open direction (exploring, nothing built)

French-first: English dropped, French-only, spelling language chosen manually. Recall ambition raised
without loosening the bar — more coverage through richer data and sentence left-context, not a looser
threshold. Corpus toward dictionary-grade coverage (Morphalou for forms, Lexique for frequency,
Wikipedia FR for context). Context to use the sentence's left context up to the last full stop, via a
model still under exploration. Rewrite (generative regeneration, keyboard or dictation) is a separate,
later chantier.

## 2026-06-13 — Observation live landed (the harvest)

The `harvest` CLI verb persists two filtered signal streams — backspace-retape correction pairs and
committed words the French lexicon does not know — DPAPI-encrypted at rest (CurrentUser scope), in the
CLI project only (the engine and its "only persisted text is the personal dictionary" invariant are
untouched). Opt-in, password-gated at the source like `watch`, passive (never mutates the personal
dictionary). Inspect/purge through `harvest list | purge | path`; the file is ciphertext, `harvest
list` is its only readable surface. This is the bridge to the typo/spelling phase: the ASR eval
corpora carry missing accents but no keyboard typos, so the retype pairs are the only real source of
typed-error material.

Capture filter (`HarvestFilter`): alphabetic tokens, length 2–24, connectors (apostrophe, hyphen)
interior only — drops digit-bearing tokens, over-long blobs, and trailing-apostrophe elision markers
(`l'`, `qu'`). Accepted residual, not eliminated: a short all-letter passphrase typed into a field that
fails to set UIA IsPassword is still captured (the keylogger class) — mitigated by the IsPassword gate,
the content filter, DPAPI, and easy purge, never by shape alone.

Trap shared with `watch`: the surface gate reads a `surface` updated only on focus change, so the
EVENT_OBJECT_FOCUS-before-first-keystroke ordering can let a single pre-focus keystroke through; bounded
to a fragment (never a committed word, so nothing reaches disk on its own), and `surface` is published
without volatile semantics in both commands.
