---
description: Dated decisions and findings for Deckle.Input.Autocorrect — founding choices, calibration measurements, deferred questions.
type: module-journal
---

# JOURNAL — Deckle.Input.Autocorrect

## 2026-06-12 — Founding decisions

- Observation fork closed with Louis: Raw Input + targeted UIA reads, repair via SendInput after the
  word commits, Enter = a pure reset. The low-level hook (`WH_KEYBOARD_LL`) was rejected — it pays
  permanent system-wide latency and Windows silently removes it after timeouts (the PowerToys Quick
  Accent failure class) for a gain confined to send contexts; a TSF text service is in-proc COM C++
  loaded into every GUI process, out of reach for a managed v1. Accepted cost: the word right before
  an Enter leaves uncorrected — to re-open if live usage shows it is the dominant miss.
  The revision-window question (correcting words behind the caret with later context) is
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

## 2026-06-13 — First live run (Louis)

- The observation layer held up live (Notepad, Explorer, Terminal): surfaces, password gate,
  commits, edits and resets all matched the model — including a `;` boundary from a slip next to
  `l` on QWERTY. Louis ran `watch` (observe-only by design); the correction path (`run`) is still
  unexercised live.
- Direction from Louis: the focused-surface detection must be reusable by the transcription
  autopaste. The UIA primitive already sits in `Deckle.Core` (`TryDescribeFocusedElement`);
  `FocusedSurface`/`SurfaceProber` are module-local today and would move down when autopaste
  consumes them.
- The self-initiated ADR was withdrawn (ADRs are the maintainer's act — `docs/adr/CLAUDE.md`); its
  rationale is folded into the 2026-06-12 entry above.

## 2026-06-13 — Adversarial audit of the correction path (before the second live run)

Four parallel auditors read the never-live-tested `run` path. Cleared as sound: RAWKEYBOARD offsets
and the flattened INPUT struct (x64, 40 bytes — the same proven path as the transcription paste),
the hDevice==0 self-filter for our own burst (filtered before decode and before the revert check),
in-order delivery (the system input queue is FIFO: a burst cannot precede its boundary on screen),
the MTA/COM apartment shape (identical in watch and run), and the elision/apostrophe chain.

Found and fixed (commits `12c5a66`, `3f50cbb`, `0eea9bc`, `96560e8`):

- `WordCommitted` fired before the edit-window state landed, so the engine's realignment
  (`ReplaceLastCommitted`) was a proven no-op by re-entrancy: after every correction the tracker
  kept the bare form as last/previous word — the context model's `prev` diverged from its own
  calibration, and the revert held only by coincidence. State now lands before the events.
- A boundary on an empty buffer left the edit window open: Backspace after « mot␣␣ » re-opened a
  word no longer adjacent to the caret (phantom commits, screen/tracker divergence).
- Ctrl+Backspace decoded as a plain Backspace (the editing-key switch preceded the chord guards):
  one modeled character versus a whole word deleted on screen — and with a revert armed, an
  injection under a physically held Ctrl that the target reads back as word deletions.
- Commit learning ran before the policy: every corrected commit reinforced the bare typo, adopting
  it after 3-4 repetitions and silently disabling its own correction (« etait » at 0.07 ppm EN
  passes eligibility; « francais »/« ecole » escape only via EN-count pollution — per-word lottery).
  Learning now feeds only on words the engine leaves alone.
- Suppression was policy-internal: the CLI toy re-corrected immediately after a revert. The engine
  now drops any suppressed (original, replacement) pair, policy-independent.
- RevertBoost equaled the adoption threshold (3.0): the next decayed read already fell short, so
  « instant adoption » never held. Now 3.5 (~3 days of adoption without reinforcement).
- The count-keyed personalVariants cache served stale variants (adoption shifts with the decay
  clock, not only mutations) — rebuilt per lookup. `enroll add notepad.exe` could never match
  (`Process.ProcessName` is extensionless) — the extension is stripped. Injection failures were
  ETW-only (revert failures not even that) and `run` hid editable/password — all surfaced on the
  console now.

Known live risks, deliberately left for after the first validated run: the surface read between
commit and injection can be stale (worst case: a burst lands in a field clicked milliseconds
earlier — re-probe before inject is the candidate fix); the UIA probe runs inside the WinEvent
callback on the input-thread pump (a UIA-slow foreground app stalls the pump and widens the
injection race; console quick-edit selection freezes the same thread); UIPI silently swallows
bursts into elevated windows (SendInput can report success — « corrected: » printed, screen
unchanged); a keystroke in flight between the boundary and the burst corrupts the rewrite (no
read-back defense; Windows 11 Notepad's own autocorrect is the same class — disable it for tests);
the hDevice==0 filter is blind to ALL synthetic input (RDP, AutoHotkey, PowerToys remaps → silent
no-op environments; the InjectionTag is readable at RAWKEYBOARD.ExtraInformation, dataOffset+12,
unread today); two concurrent `run` instances double-inject, and the `dict` CLI flushes even on
`list` (last-writer-wins against a live run — purge with run stopped). The engine itself has no
test seam (concrete host/injector) — extracting interfaces is the prerequisite for pinning the
gate order in tests.

## 2026-06-13 — Second live run: stale binary, and two by-design misses named

- The evening's live runs all executed a CLI binary built at 00:28 — before every audit fix
  (01:15–01:22) and the trace mode (01:51). Post-fix validation had run through `Deckle.Tests.sln`,
  which does not rebuild the CLI executable; the stale-binary state was caught on the old `surface:`
  line format in the run output. The CLI was rebuilt at 01:59. Consequence: every live observation
  so far (the mangled rewrites, the intermittence) is **pre-fix** evidence — none of the audit fixes
  has been exercised live yet.
- In that same run, « ecole → école » and « francais → français » landed clean on screen (the
  remnant shows the corrected text). The mangling does not reproduce on every occurrence, on the
  same pre-fix binary.
- « ca » → « ça » never fires, by design: the EN web counts carry "ca" at 221.6 ppm (CA, ca.), far
  above the 5 ppm English-guard bar, and that guard returns before the candidate machinery — so
  even a personal-dictionary adoption could not override it (guard order: English bar precedes the
  personal-variant merge). Whether an explicit user signal should outrank the corpus prior is an
  open product question for Louis — « ça » is among the most frequent French words.
- « cedille » → « cédille » never fires for a different reason: Lexique 3.83 (raw, verified) has no
  « cédille » entry at all, so the derived lexicon offers no variant — empty candidates, the
  literal stays. The EN count (0.022 ppm) is below the guard bar, so the block is purely the
  missing target. The personal dictionary is the designed remedy for this class: typing the
  accented form to adoption (≥ 3 occurrences) both supplies the variant and shields the word.

## 2026-06-13 — Louis's call: French-first, recall raised, context promoted

The pivot, in Louis's words:

- **English dropped, French-only, language chosen manually.** No bilingual guard, no language
  detection — the user picks the spelling language. This removes the guard that blocked « ca » → « ça ».
- **Recall ambition raised — but not by lowering the bar.** « J'ai envie que je tape et que ça se
  corrige », « avec un certain niveau de sûreté ». Read carefully: he is not abandoning precision;
  he refuses precision used as an alibi for weak recall. The route to *more coverage AND safe* is
  richer data + sentence-level left-context, not a looser threshold. The module's « conservative by
  doctrine » framing is to be reworked in that light — pending the domain survey, not rewritten yet.
- **Corpus must reach dictionary-grade coverage.** Lexique 3.83 (subtitle/book frequencies) misses
  ordinary words like « cédille ». Louis wants a French source with full word coverage — academic /
  dictionary sources to be surveyed. Nothing chosen.
- **Context promoted: the preceding words up to the last full stop.** Louis wants the correction
  decision informed by sentence context (« les quelques mots qui précèdent jusqu'au prochain point »),
  via models — to be both safer and more covering. This supersedes « on passe le contexte au suivant »:
  context is now central, not deferred. Spectrum of local models (n-gram / small neural / local LLM)
  to be weighed — note the standing « LLM rewrite paused » decision applies to dictation rewrite, a
  different surface; this is disambiguation, not rewrite.
- **Input automatisms wanted.** Double-space → period, auto-capitalization, and the like — the
  « smart typing » features of mature input systems. New scope, to be specified.
- **Method:** the field is mature (~20 years); survey the best practices before re-architecting.
  Domain survey launched 2026-06-13 (workflow `autocorrect-domain-survey`) — a French état-de-l'art
  briefing to read before choosing corpus and context architecture. No code until then.
