---
description: Dated decisions and findings for Deckle.Input.Autocorrect — founding choices, calibration measurements, deferred questions.
type: module-journal
---

# JOURNAL — Deckle.Input.Autocorrect

## 2026-06-12 — Founding decisions

- Observation fork closed with Louis: Raw Input + UIA, repair after commit, Enter = reset (ADR-0018,
  proposed). The revision-window question (correcting words behind the caret with later context) is
  **deferred to the context stage** — at the lexical-gate stage only the just-committed word is
  touched, so nothing is revised behind the caret in v1.
- Module named `Deckle.Input.Autocorrect`, domain module of the input pillar, mirroring
  `Deckle.Input.Trackpad`. The keyboard/mouse Raw Input host lands in `Deckle.Input` (support layer)
  as `KeyboardInputHost`, separate from the touchpad `RawInputHost` — merging the two pumps is a
  later refactor question, not a v1 constraint.
- Enter performs a pure reset: no commit event, no learning signal. Conservative — a send context may
  have dispatched the text; counting it would learn from words we never saw committed.
- Elision rule for apostrophes: an apostrophe after a known elision prefix (l, d, j, n, m, t, s, c,
  qu, jusqu, lorsqu, puisqu, quoiqu) commits the prefix as its own token; otherwise the apostrophe
  joins the word (aujourd'hui). Both ASCII U+0027 and typographic U+2019 count.
- Learning constants are ours to calibrate (AOSP's vary across eras): adoption at effective
  weight ≥ 3.0 (cadrage decision: ~3 occurrences), half-life 14 days, cap 5 000 entries. Revert =
  immediate suppression entry + strong adoption boost on the literal.
- Data choices: Lexique 3.83 (CC BY-SA 4.0 — verified, the NC mentions are a Flexique confusion)
  rather than Hunspell expansion; Norvig count_1w as the English bilingual guard; Wikipedia FR via
  the MediaWiki extracts API for pair bigrams, train/eval held out by article. The LINDAT corpus is
  NC — not used.
