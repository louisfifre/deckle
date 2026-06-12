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

## 2026-06-13 — Offline evaluation (Wikipedia FR eval split, 257 019 tokens)

Derived artifacts: lexicon-fr 125 343 forms (per-ortho sums of freqfilms2/freqlivres, max of the
two), lexicon-en 333 333 words, pair model 3 387 slots / 11 085 rows from 1.31 M training tokens.
Artifacts are byte-deterministic (verified by double-run SHA-256).

- **Operating point shipped as defaults** — EN guard 5 ppm, dominance 20×, context margin 10× /
  evidence 5 / literal bias 2×, valid-forms off. Measured: **56.0 %** of omitted accents restored,
  **132 false corrections / 217 300 bare words (0.061 %)**, 116 wrong variants, word accuracy
  93.15 % (doing nothing scores 84.5 %).
- Found: the context **margin is the lever** that kills wrong-variant picks — 342 → 195 → 116 at
  3×/6×/10× for −1.2 pt of recall vs 3×; the evidence floor is inert past 5. Defaults moved to 10×.
- Found: the EN guard trades ~9 pts of recall for false corrections 720 → 132. Bare-stripped French
  lives in the English web counts: at 0.5 ppm the guard blocks même/être/déjà (recall collapses to
  48 %); at 50 ppm English proper nouns get frenchified (Leonard→Léonard). 5 ppm kept; the visible
  cost is the borrowed-word class (role→rôle, hotel→hôtel, cinema→cinéma stay uncorrected).
- Found: valid-forms mode (the a/à real-word class) at margin 10 buys +0.55 pt of recall for +28
  false corrections on common, visible verbs (utilise→utilisé, marche→marché, donne→donné).
  Kept off in the live engine — matches the conservativity doctrine; the class stays measured.
- Found: an irreducible false-correction floor (~37 at the strictest guard) comes from the corpus
  itself — 1990-reform spellings (connait, chaine, frontiere) that Lexique writes with accents. A
  lexicon/corpus disagreement, not an engine bug.
- Reading the gap to the 95-98 % literature headline: those operating points correct valid bare
  forms (à, où, là — the single biggest missed chunk here) and carry no bilingual guard. v1 sits
  deliberately below the headline, on the other side of the false-correction trade.

Deferred, noted while wiring: the « come back later » variant of the correction revert needs caret
position knowledge (v1 arms the revert for the immediately-next keystroke only); the enrollment
prompt waits on the notifications brick (CLI `enroll` stands in); a *relative* EN guard (block only
when the EN frequency dwarfs the FR variant's) could recover the borrowed-word class — unmeasured.
