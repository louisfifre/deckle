---
description: Dated decisions and findings for Deckle.Autocorrect — founding choices, measurements, open direction.
type: module-journal
---

# JOURNAL — Deckle.Autocorrect

## 2026-07-04 — The stack is split across two runtime worlds; direction is DirectML with GenAI alongside

Grounded in the current code (on main): the four inference stages do not share a runtime. Reranker (CamemBERT, commit + contextual) runs on plain ONNX Runtime — an `InferenceSession` encoder, CPU-bound and single-threaded (`BackgroundRerankLane`). The sentence judge runs on onnxruntime-genai (CPU int4 today, DirectML targeted). Rewrite runs on Ollama (llama.cpp / ggml). ASR runs on whisper.cpp (native ggml), not ONNX. So two stages live in the ONNX world and two in the ggml/Vulkan world — the latter being the 7900 XT's strong GPU path. The "run everything under the same system" wish is therefore a real substrate fork (ONNX/DirectML vs ggml/Vulkan), left open, not a settled convergence.

Maintainer direction, firm: build the DirectML path for the judge now; onnxruntime-genai is used alongside — for the autoregressive judge/rewrite — not as the substrate for everything.

## 2026-07-03 (later) — Judge runtime stays ONNX; the DirectML block was a wrong export, not a wrong backend

Refines the DirectML note below. DirectML does run blocked int4 — Microsoft ships Phi-3 as int4 AWQ block-128 on the DML EP. The block was reusing the int4 *CPU* export: a CPU int4 graph and a DML int4 graph differ (MatMulNBits weight layout, accuracy level), so ORT cannot assign the nodes to the DmlExecutionProvider and silently falls back to CPU — a partition miss, not a graph-capture failure. The unblock is a model-builder `-e dml` export from a 0.13.x builder, matched to the pinned 0.13.0 runtime. The 0.14 `DllNotFoundException` is a NuGet packaging break (DirectML 0.14.1 depends on a Managed 1.23.x that does not exist), not a model fault.

Maintainer direction, firm: the judge stays on ONNX Runtime GenAI — closer to Windows, small and efficient — so the LLamaSharp/Vulkan/GGUF path is set aside, though it was verified viable for logprob scoring on the 7900 XT. The judge should offer a size × provider matrix — 0.6B on CPU and 1.7B on GPU as defaults, any size on either provider — so all sizes stay available. Luth stays the French candidate: Luth-0.6B/1.7B are Qwen3 fine-tunes (Apache-2.0, same conversion path), reported above their Qwen bases on French benchmarks; GGUF quants public, no ONNX export yet.

Open symptom: a calibration run went about 20 minutes with no visible output before being stopped — plausibly the test platform buffering the gesture's stderr to the end rather than a hang. The next session is a deliberate grill over the whole judge system: why an export runs under one provider and not the other, and why the runtime question kept looping.

## 2026-07-03 — The sentence judge needs a GPU-built export to run on DirectML

The Qwen3-4B CPU int4 judge over the 404-sentence typed corpus took about 23 minutes at roughly 6 of 16 cores — CPU int4 is an offline-only path, live-unviable at seconds per sentence. The scorer now selects its execution provider in code (`Config.ClearProviders` + `AppendProvider`, as PhiBench does), so one export could in principle be driven onto the GPU without a re-export, and the replay streams per-slot progress to stderr. But DirectML, while it loads (no `DllNotFoundException`), cannot run the CPU int4 exports: session init fails because the int4 `kld-block-128` MatMul nodes do not partition to the `DmlExecutionProvider`, and graph capture then demands all nodes on DML. The GPU path needs a model-builder `-e dml` export, not the CPU int4 one. `onnxruntime-genai` DirectML is pinned to 0.13.0 — 0.14 wants ORT API 24 but bundles ORT 1.23, a mismatch that throws `DllNotFoundException` on init (PhiBench's finding).

## 2026-07-03 — ONNX judge scoring now includes the suffix evidence

Found that scoring only the discriminating middle removed useful evidence from the shared suffix, such as `personne` after `la`/`là`. The ONNX judge now scores from the first candidate divergence through a newline answer delimiter. On the hardened 30-case benchmark, Qwen3-4B CPU int4 took about 546 s; at margin `0.25` it made one false literal change and fixed 13/14 correctable cases, while at margin `0.5` it made no false change and fixed 12/14. Qwen3-0.6B took about 107 s; at margin `0.5` it made no false change and fixed 7/14.

## 2026-07-03 — Closed-candidate Qwen bench rejects silent use

Benchmarked 30 closed French correction cases against staged Qwen3 ONNX Runtime GenAI CPU int4 models with the forced-logprob judge. At margin `0.25`, 0.6B and 1.7B each made one high-margin false literal change, 4B made one high-margin false literal change, and 8B made two high-margin false changes; no tested Qwen size is safe for silent sentence correction without another guard. Timings for 30 cases were about 68 s, 171 s, 360 s, and 647 s.

## 2026-07-03 — Qwen 4B baseline, Luth benchmark before runtime change

Chose Qwen3-4B ONNX Runtime GenAI CPU int4 as the immediate closed-candidate judge baseline. Luth stays a candidate because its French-specialized 0.6B/1.7B models are public and benchmarked above their Qwen bases on French tasks, but the verified ready artifacts are Hugging Face `safetensors` and GGUF quantizations, not an ONNX Runtime GenAI export. Next step is an offline Luth benchmark on the same closed-candidate traps before either converting Luth to ONNX GenAI or adding a GGUF/llama.cpp runtime.

## 2026-07-03 — ONNX GenAI scorer kept outside the engine

Chose a separate `Deckle.Autocorrect.Onnx` module for the first ONNX Runtime GenAI sentence scorer. The scorer compares closed full-sentence candidates by forced token log-probabilities and returns margin plus abstention reason; live sentence-stage integration and DirectML remain outside this first proof.

## 2026-07-03 — Qwen first, visible probe before live wiring

Chose Qwen3-0.6B as the first smoke-test model and Qwen3-1.7B as the next audition if the 0.6B signal is weak. Added a console probe before live integration so model load, candidate scores, margin and abstention are visible before any silent correction path can use them.

## 2026-07-03 — Qwen 0.6B needs judge prompting, not raw likelihood

Staged `onnx-community/Qwen3-0.6B-ONNX` CPU int4 ORT GenAI locally and smoke-tested closed candidates through the probe. Raw sentence likelihood picked the wrong side on `il a dit` / `il à dit`; label-only judging exposed a strong position bias. Kept the useful shape: a judge prompt listing closed variants, forced scoring of exact candidate answers, and an order-normalized pass. With a margin such as `0.25`, `la/là`, `a/à`, and `ou/où` pass clearly, while weak `er/é` evidence abstains instead of silently applying.

## 2026-07-03 — Candidate scoring moved to the discriminating span

Found that full-answer average log-probability still carries noise from shared words. The ONNX judge now plans each candidate completion into common prefix, discriminating middle, and common suffix; it feeds the common prefix but scores only the discriminating middle. On Qwen3-0.6B CPU int4 this sharpened accent/homophone margins while keeping weak `er/é` evidence below the abstention threshold.

## 2026-07-03 — Qwen CPU int4 model audition started

Staged `onnx-community/Qwen3-0.6B-ONNX`, `Qwen3-1.7B-ONNX`, `Qwen3-4B-ONNX`, and `Qwen3-8B-ONNX` CPU int4 ORT GenAI exports locally. Their staged sizes are about 0.5 GB, 1.32 GB, 2.7 GB, and 5.67 GB. On the `rien de cassé` / `rien de casser` trap, 0.6B and 1.7B chose the wrong infinitive, 4B chose the participle, and 8B abstained at margin `0.25`.

## 2026-07-02 — Implementation grill: fine behavior and order settled

Order: function first, interactions last. The injection fix, the purge of both poisoned stores, the removal of the implicit Backspace revert and a clean restart of the collection datasets land in one gesture; the correction inlay (and with it one-gesture undo and negative learning) closes the chantier instead of opening it — during the functional phase the only recourse against a bad correction is manual re-editing, accepted.

Sentence stage mechanics: one pass at sentence close, never continuous — all revisions concentrate on a single injection point. The candidate set includes the typed original, so a commit-stage false positive on an unknown token (an English word not yet protected) silently repairs itself at the close; the transient flicker is assumed. A verdict arriving after any keystroke since the close is dropped and counted. A word the user reopened and retyped is exempt from commit-stage re-correction only — the sentence stage keeps full rights (agreement fixes need context). Edge cases stay on the simplest default; refinement only where the collected data shows a real bite.

Lexicon: three tiers (swappable primary language — French only shipped, language choice a future chantier; permanent restricted global-English; personal vocabulary). One adoption mechanism; the English tier adds only a fixed cold-start seed — candidate sourcing: a SUBTLEX-US JSON derivative (ISC) truncated high, crossed with FranceTerme (Etalab), filtered against forms that are French words or plausible French typos; own-corpus extraction via token-level LID as the long-term source. Adoption: recurrence (≈3 occurrences over ≥2 distinct days, threshold to calibrate on the corpus) plus the cleanliness gate (typed verbatim, never reopened-retyped, surface-clean); categories anglicism / proper noun (case-sensitive) / other.

Observation: each sentence slot records its ordered history — first-typed form, then each transition tagged by author (commit stage, sentence stage, user) — final at sentence close; post-close edits are invisible (the tracker has no caret, accepted). Fate counters derive directly: kept / re-edited-in-sentence (the personal WMR) / dropped verdicts / doubt-literals. Field names describe the technical function, never the interpretation; full capture, compact log rendering. Pre-fix corpus and stores are discarded as suspect. Minimal harness: a replay runner (module-lib, test-exercised) diffing decisions between engine versions over the typed-sentence corpus; ERRANT-FR porting and the LLM-judge wait for corpus volume.

Engine: the internal ONNX runtime enters this chantier as its final phase — a small judge model auditioned ONNX-local (Luth/Qwen3 0.6–1.7B class, logprobs over bounded candidates, onnxruntime-genai C#); Ollama serves during construction and remains a pluggable option afterwards. The personal LoRA over typed→corrected pairs is the next chantier; its corpus is already accumulating. Dormant artifacts (verbs-fr.tsv.gz, the Morphalou overlay call) regenerate through the maintainer's build-data gesture.

## 2026-07-02 — Globish seed source chosen

Chose FranceTerme-only for the cold-start globish seed: `lexicon-en-globish.tsv.gz` is generated from `Equivalent langue="en"` in `FranceTerme.xml`, with common function words and French exact/accent-fold/one-edit collisions filtered out. SUBTLEX derivatives and wordfreq stay out of the shipped artifact until their licence/propagation questions are explicitly accepted.

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
