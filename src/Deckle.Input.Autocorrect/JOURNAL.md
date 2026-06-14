---
description: Dated decisions and findings for Deckle.Input.Autocorrect — founding choices, measurements, open direction.
type: module-journal
---

# JOURNAL — Deckle.Input.Autocorrect

## 2026-06-12 — Founding architecture

Observation fork closed: Raw Input + targeted UIA reads, repair via SendInput after the word commits, Enter = a pure reset (a send context may already have dispatched the text). The low-level hook was rejected — permanent system-wide latency and silent removal after timeouts, the PowerToys Quick Accent failure class; a TSF text service is in-proc COM C++, out of reach for a managed v1. Accepted cost: the word right before an Enter stays uncorrected.

## 2026-06-13 — Design axis and test seam

The design axis is the false-correction-vs-recall trade; the context margin is the lever against wrong-variant picks (doing nothing scores 84.5 % word accuracy on a Wikipedia FR split). Test seam extracted — three OS ports (`IKeyboardInputHost`, `ISurfaceProber`, `ITextInjector`) run the real engine against a faked OS. Unresolved live risks left for later: stale surface read before inject, UIA probe on the input pump, silent UIPI into elevated windows, keystroke-in-flight corruption, self-filter blind to other synthetic input, double-inject from two run instances.

## 2026-06-13 — Reranker and the telemetry corpus

Eval corpus built from the app's own transcription telemetry (real French-dense distribution with English tech terms). CamemBERT reranker over the lexical gate, with a frequency prior (keeps the common form unless the model strongly overrules) and a proper-noun caps guard (leaves a title-cased mid-utterance word alone). Two durable findings: the ASR ground truth writes « çà » for « ça » — a reference artifact to clean before using telemetry as an eval reference; and the gate's false corrections split into capitalized proper nouns (caps guard) and verb conjugations absent from Lexique (the coverage gap).

## 2026-06-13 — French-first pivot

English dropped, French-only, spelling language chosen manually. Recall ambition raised without loosening the bar — more coverage through richer data and sentence left-context, not a looser threshold. Rewrite (generative regeneration) is a separate, later chantier.

## 2026-06-13 — Observation live (the harvest)

Why it exists: ASR eval corpora carry missing accents but no keyboard typos, so backspace-retype pairs are the only real source of typed-error material. The `harvest` verb persists two filtered streams (retype pairs, lexicon-unknown words — never the raw stream), DPAPI-encrypted, CLI project only, opt-in, password-gated, passive. Two traps: a short all-letter passphrase typed into a field that fails to set UIA IsPassword is still captured (mitigated by gate + filter + DPAPI + purge, never by shape); and the surface gate reads a `surface` updated only on focus change, so a single pre-focus keystroke can slip through (bounded to a fragment, never a committed word).

## 2026-06-14 — Margin and Morphalou, decided

Margin stays at 2: the recall ceiling on the à/là/où class is the MLM's own confidence, not the threshold, and lowering it trades precision for forced wrong accents. Morphalou overlay is opt-in (`build-data --morphalou`); the default ships Lexique-only. A broad overlay hurts recall — the extra forms crowd previously-unambiguous folds and demote clean restorations into the reranker, which abstains at margin 2. Morphalou's real value is literal protection of valid conjugations against false correction, not coverage ranking.

## 2026-06-14 — Chromium/Electron editable surfaces

Broaden focused-element editability beyond the Edit/Document control types to UIA control patterns (the TextEdit pattern, a writable Value, or a Text provider), so a Chromium/Electron contenteditable composer — a Group control (50026) exposing the Text/TextEdit pattern, as Claude and Anytype do — is seen as editable and gets corrected; the strict gate had silently withheld every correction there. The paste probe (IsFocusedElementTextEditable) stays Edit/Document only. SurfaceChanged now carries the raw UIA signature for auditing.
