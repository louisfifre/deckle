---
description: Dated decisions and findings for Deckle.Autocorrect — founding choices, measurements, open direction.
type: module-journal
---

# JOURNAL — Deckle.Autocorrect

## 2026-07-02 — Injection corruption root-caused in the decision telemetry

The reported symptoms (doubled apostrophes « j''ai », eaten letters « pasé », polluted words « qu''il ») trace to two code paths. First: `SentenceRerankCoordinator.ApplySlotRewrite` rebuilds the on-screen tail as `Form + Boundary` per slot, but an elision commit carries the apostrophe both inside the form (« j' ») and as the boundary — the rebuilt tail overstates the screen by one char per elision, so the injection plan sends one extra backspace (eaten letter) and retypes a doubled apostrophe. Second: the first Backspace after an elision commit re-opens the committed word one char out of alignment (`TypedWordTracker.ProcessBackspace` assumes a separate boundary char that an elision commit never displayed). Collateral in the learning stores: the personal dictionary adopted injection artifacts, and the punctuation-misfiring revert wrote permanent suppressions of legitimate corrections (cable→câble, arrete→arrête, suppor→support, fonctionnalite→fonctionnalité). Decided: both stores are purged when the injection fix lands. Character inversions remain unproven — plausibly the stale-partial race during the 3–8 s deferred-apply window.

## 2026-07-02 — Behavior contract redecided (grill session)

The chantier's finish line moves from delivered bricks to silent trust: typing requires no attention, English tokens in French prose included. Decisions, normative vocabulary in `CONTEXT.md` § Autocorrect and § Text operations: the implicit Backspace revert is retired — Backspace always just deletes; a correction is taken back only through the correction inlay, which also carries the negative learning signal. The two-stage model is assumed: commit stage (instant, left context only) and sentence stage (deferred, whole-sentence context, may revise committed words inside the current sentence only) — injection robustness is a blocking prerequisite of the latter. Silent application is reserved for bounded edits whatever engine computed them; free regeneration is Rewrite and never silent. English returns as a *restricted* protected lexicon (technical globish, brands, grown from the user's own usage), never a correction target; FR-vs-EN is a sentence-stage call. Rewrite is mutualized into one service (transcription finalization, paragraph rewrite, sentence-stage escalation); target runtime ONNX is decided, Ollama serves until that migration. Personal-vocabulary adoption requires a cleanliness gate and a recurrence threshold; the ASR bridge stays out of this chantier. Paragraph rewrite triggered on \n / Shift+Enter is noted and deferred.

Grammar/conjugation correction is a home-grown deterministic rule engine over Lexique morphology, in-process, no JVM — not LanguageTool embedded or ported. LanguageTool's ~7000 French rules match on POS tags, not surface, so they are inseparable from its Java tagger/disambiguator; "recover existing rules" is taken as recovering the rule knowledge for a safe subset, expressed in our own engine, the morphology lifted from Lexique 3.83 (CC BY-SA, already vendored).

The verb morphology Lexique carries but build-data dropped is now emitted as verbs-fr.tsv.gz (form, lemma, infover codes, a verb-only flag read off cgramortho) and read back through VerbMorphology (readings per form, reverse conjugation, the ambiguity guard). First rule: subject–verb agreement on an adjacent subject-only pronoun (je/tu/il/elle/on/ils/elles; nous/vous excluded — they double as object clitics), firing only on a verb-only form with a unique agreeing target, else the literal stands. Wired last in the composite, optional — absent the artifact the chain runs as before.

é/er/ez (infinitive vs participle vs 2nd-plural) is deferred: the safe trigger set is a calibration question — the "rien de cassé" adjectival trap after "de", participles tagged ADJ — better grounded on the live decision telemetry than guessed.

Note: LexiconBuilder.Run has no in-repo caller; the artifact regenerates through the maintainer's build-data gesture.

## 2026-06-18 — Revert in the decision dataset

The revert gesture now leaves a structured record (AutocorrectRevertRecorded, id 20) in autocorrect.decisions.jsonl, joined to the correction it undoes by the per-word id, under the same AutocorrectDecisions gate — no new toggle. It carries the pair, the consumed boundary char, its kind (whitespace/punctuation/apostrophe/other) and the commit→revert delta. The current discriminator (first Backspace within 2 s) only ever consumes the trailing boundary, so a `punctuation`-kind revert is the signature of the known misfire — deleting a misplaced comma/period, misread as an undo. This is the diagnostic precursor; the discriminator fix (fire only when the Backspace bites into the corrected word) is deferred until the data confirms.

## 2026-06-18 — Typed-sentence corpus

Second opt-in text dataset (autocorrect.text.jsonl), nested under the decision toggle, off by default: per sentence typed at the keyboard on an enrolled surface, the verbatim typed form paired with the corrected one — the substrate for modelling the user's own error patterns. A pure SentenceCorpus accumulator rebuilds the sentence from the word-commit stream: boundary chars rejoin the words so a ';'-for-apostrophe substitution survives on both sides, the elision apostrophe collapses to avoid doubling, manual re-edits fold into their slot, and any run interrupted before a sentence end is dropped (paste and dictation never reach the word stream). Direction: maximise the reranker first, an LLM later. Durable finding: the keyboard-substitution class (';' for an apostrophe) sits below the reranker — it splits the token at tokenisation — so it needs a pre-tokenisation repair, not reranker tuning.

## 2026-06-18 — Per-word decision telemetry; the text-free rule rescoped

The « typed text never crosses the EventSource » rule is rescinded as an absolute and rescoped: the default path (app.jsonl) stays text-free, but words may cross on explicit opt-in. A per-word decision dataset (autocorrect.decisions.jsonl) records each evaluated word with its candidates, scores, margins and the deciding guard, behind a settings toggle + consent dialog, excluded from app.jsonl, off by default, password-gated. Two new Verbose/Heartbeat events (AutocorrectDecisionRecorded, AutocorrectRerankRecorded) carry the text; the synchronous decision and the deferred reranker verdict join on a per-word id.

## 2026-06-14 — Chromium/Electron editable surfaces

Broaden focused-element editability beyond the Edit/Document control types to UIA control patterns (the TextEdit pattern, a writable Value, or a Text provider), so a Chromium/Electron contenteditable composer — a Group control (50026) exposing the Text/TextEdit pattern, as Claude and Anytype do — is seen as editable and gets corrected; the strict gate had silently withheld every correction there. The paste probe (IsFocusedElementTextEditable) stays Edit/Document only. SurfaceChanged now carries the raw UIA signature for auditing.

## 2026-06-14 — Margin and Morphalou, decided

Margin stays at 2: the recall ceiling on the à/là/où class is the MLM's own confidence, not the threshold, and lowering it trades precision for forced wrong accents. Morphalou overlay is opt-in (`build-data --morphalou`); the default ships Lexique-only. A broad overlay hurts recall — the extra forms crowd previously-unambiguous folds and demote clean restorations into the reranker, which abstains at margin 2. Morphalou's real value is literal protection of valid conjugations against false correction, not coverage ranking.

## 2026-06-13 — Observation live (the harvest)

Why it exists: ASR eval corpora carry missing accents but no keyboard typos, so backspace-retype pairs are the only real source of typed-error material. The `harvest` verb persists two filtered streams (retype pairs, lexicon-unknown words — never the raw stream), DPAPI-encrypted, CLI project only, opt-in, password-gated, passive. Two traps: a short all-letter passphrase typed into a field that fails to set UIA IsPassword is still captured (mitigated by gate + filter + DPAPI + purge, never by shape); and the surface gate reads a `surface` updated only on focus change, so a single pre-focus keystroke can slip through (bounded to a fragment, never a committed word).

## 2026-06-13 — French-first pivot

English dropped, French-only, spelling language chosen manually. Recall ambition raised without loosening the bar — more coverage through richer data and sentence left-context, not a looser threshold. Rewrite (generative regeneration) is a separate, later chantier.

## 2026-06-13 — Reranker and the telemetry corpus

Eval corpus built from the app's own transcription telemetry (real French-dense distribution with English tech terms). CamemBERT reranker over the lexical gate, with a frequency prior (keeps the common form unless the model strongly overrules) and a proper-noun caps guard (leaves a title-cased mid-utterance word alone). Two durable findings: the ASR ground truth writes « çà » for « ça » — a reference artifact to clean before using telemetry as an eval reference; and the gate's false corrections split into capitalized proper nouns (caps guard) and verb conjugations absent from Lexique (the coverage gap).

## 2026-06-13 — Design axis and test seam

The design axis is the false-correction-vs-recall trade; the context margin is the lever against wrong-variant picks (doing nothing scores 84.5 % word accuracy on a Wikipedia FR split). Test seam extracted — three OS ports (`IKeyboardInputHost`, `ISurfaceProber`, `ITextInjector`) run the real engine against a faked OS. Unresolved live risks left for later: stale surface read before inject, UIA probe on the input pump, silent UIPI into elevated windows, keystroke-in-flight corruption, self-filter blind to other synthetic input, double-inject from two run instances.

## 2026-06-12 — Founding architecture

Observation fork closed: Raw Input + targeted UIA reads, repair via SendInput after the word commits, Enter = a pure reset (a send context may already have dispatched the text). The low-level hook was rejected — permanent system-wide latency and silent removal after timeouts, the PowerToys Quick Accent failure class; a TSF text service is in-proc COM C++, out of reach for a managed v1.
