---
description: Dated decisions and findings for Deckle.Autocorrect — founding choices, measurements, open direction.
type: module-journal
---

# JOURNAL — Deckle.Autocorrect

## 2026-07-21 — Closed-sentence writes replace live-tail rewrites

Found a visible corruption where the internal final corpus was correct but the target field received duplicated characters. Two Deckle 0.14.4 residents were active simultaneously, the sentence stage applied mid-sentence after three right-context words, and its correction detail falsely reported zero backspaces regardless of the actual write plan.

Chose one resident process per Windows session through a stable named mutex; install, install-continuation, update-apply and data-relocation modes remain exempt. Normal restarts now stop resident services and release ownership before spawning their successor.

Restricted the sentence stage to terminal punctuation (`.`, `!`, `?`, `…`). It no longer runs after a word-count deferral, on semicolon or colon, on a typing pause, or for delayed deterministic capitalization. The first physical key after closure invalidates the epoch, and a non-empty live partial at delivery also drops the verdict. Sentence rewrites now report the actual computed injection plan to operational telemetry.

## 2026-07-20 — Reliability precedes the Qwen 3.5 correction stage

Chose to stabilize the correction path before integrating Qwen 3.5: make the active engine and load failure observable, remove silent fallback, then fix surface tracking and sentence completion. The intended product split is a conservative deterministic first level for accents and certain spelling repairs, followed by Qwen 3.5 for grammatical correction; rewrite remains later.

Found in the live trace that `models\sentence-judge` was present but the application reported `engine=camembert`. On `je n'arrive pas a choisir`, CamemBERT ranked `à` first with a 1.291 margin and abstained at its 2.0 threshold. The same Qwen3-1.7B DML export loaded in the isolated probe and chose `je n'arrive pas à choisir` with a 1.026 margin at its 1.0 threshold. `OnnxSentenceScorer.TryLoad` catches every load exception without recording it, so the application exposes neither the Qwen failure nor the fallback cause.

The application asset graph currently resolves ONNX Runtime 1.26.0 alongside ONNX Runtime DirectML 1.23.0 and ONNX Runtime GenAI DirectML 0.13.0; the isolated probe resolves the 1.23 runtime and loads the judge. This version difference is a lead, not yet the proven load cause.

Found in the running Release process that GenAI 0.13 was loaded beside native ONNX Runtime 1.26 although its package depends on ONNX Runtime DirectML 1.23; DirectML itself had not loaded. Chose one process-wide ONNX Runtime DirectML 1.23 binary: ordinary inference sessions still select the CPU provider, while the sentence judge explicitly selects DML.

Verified the unified build in the live application: native ONNX Runtime 1.23 and DirectML loaded, the sentence judge became the active contextual engine in 2.114 s, and no load failure was emitted.

Found two independent timing traps in the input path. `FocusEventCoalescer` publishes a foreground event immediately and suppresses the following object-focus event for the same HWND within 50 ms, while the surface is probed on the first event; a web editor that establishes its focused element between the two can leave Deckle with the earlier surface verdict. Separately, Enter, pointer or focus reset invalidates the sentence epoch, so a still-running sentence verdict is dropped as stale. The Codex edit-message field from the observed failure was nevertheless recognized as editable and its text was captured; that occurrence was a reranker abstention, not a field-detection failure.

Chose to make sentence-stage abandonment an immediate, explicit transition: a reset with unresolved work records its reason, pending-slot count and whether inference was in flight. Raw Input still cannot retain Enter; Deckle therefore applies only verdicts returned before the send/reset gesture and abandons the rest instead of discovering that loss later from a generic stale result.

## 2026-07-14 — Mining chantier: routing and pause pass landed

Approved families are per-user records interpreted by code kinds (boundary_apostrophe, boundary_missing_space) — nothing personal frozen in code; the tracker surfaces the inter-commit separator run, invalidated whenever a backspace or reset makes it unfaithful, and span repairs inject at commit, honor suppressions, and edit the corpus final-side separator (typed keeps the fault). The pause pass flushes open slots on a typing pause (one re-armed one-shot timer through the drain marshal); pause verdicts are re-reviewed at true closure, Enter cannot re-review — the measured residue. Per-surface bars ride surface-profiles.json from the ventilation gesture; the provisional qualification formula (Enter-dominant, ≥30 timed sentences, gap p99) is quarantined in SurfaceProfiler pending calibration. First mined batch reviewed: two families approved (sub ;→', dropped space after ,).

## 2026-07-14 — Typing stream capture landed

Runs emitted per closure with the erase count on the following record (a dangling repair flushes text-less); closure vocabulary: repair/cap continue a span, enter/navigation/escape/shortcut/delete/deadkey/pointer/focus end it. Feed gate is editable + enrolled + consent, tighter than the sentence corpus; same AutocorrectText envelope, new `autocorrect_stream` kind, run cap 512 chars. Per-char keystroke gaps ride each run — the first gap after a repair includes the repair pause.

## 2026-07-14 — Mining chantier framed (grill session)

Decisions, normative vocabulary in `CONTEXT.md` § Autocorrect: mistouch families are mined offline from the typed-sentence corpus and expressed as deterministic detector-generators — the judge only ever scores bounded candidates, never proposes. Routing follows ambiguity; commit-stage eligibility takes three cumulative conditions (impossible non-word trigger, unique repair, left context suffices), everything else feeds the sentence-stage judge. Families adopt automatically past a corpus-calibrated evidence threshold under the personal dictionary's discipline (inspectable, removable, undo writes suppression); the first mined batch alone is maintainer-reviewed. The typing stream captures everything typed on enrolled surfaces — runs segmented at backward repairs — serving both the error corpus and a natural-language corpus; same consent envelope, one JSONL kind among the autocorrect datasets; password surfaces stay outside, as everywhere. Surface profiles (closure/timing per application) calibrate the pause pass — an anticipated sentence-stage pass on a typing pause, so corrections land before the Enter that sends. Chantier order: typing stream first (corpus accrues while the rest is built), profile ventilation, miner, routing wiring, pause pass last. Evidence and pause thresholds are calibrated on the data, never guessed. ADR deferred until tested.

## 2026-07-14 — Final-punctuation re-edit can outrun corpus attribution

Found that a manual re-edit ending immediately with sentence-final punctuation can close and emit the sentence before `WordEdited` reaches `SentenceCorpus`; the corrected slot may therefore retain the pre-edit attribution. A clean fix requires changing the tracker-to-corpus commit contract rather than delaying punctuation handling.

## 2026-07-14 — The sentence judge is live, margin 1.0, decided

The ONNX GenAI sentence judge (Qwen3-1.7B DML int4, `models\sentence-judge`) is wired into the live sentence stage, preferred over CamemBERT when its model directory is present, through the existing background rerank lane (single-flight, epoch-dropped staleness). Operating margin decided at 1.0 on the 2026-07 replay calibration (979 slots, maintainer truth overlaid): 92.2% precision at 20.8% coverage, against 90.8%/41.0% at 0.5 — chosen precision-first for the live start, to be relaxed as the widened corpus grows. Replay judged whole sentences while live sees ±12 words, so calibration precision is slightly optimistic for live. Margins remain per-export (see 2026-07-04): this one binds to the DML int4 export the live path loads.

Trap, measured while validating: GenAI Model construction on the DML provider fails transiently with "Specified provider is not supported" — same binary and machine, next attempt clean; it surfaced under the parallel test host but one bare run also failed after the suite went serial, so it is a load flake, not (only) a concurrency artifact. Guards: one bounded retry in the scorer's model construction (every consumer inherits it), and the tests that open a real GenAI session run in a non-parallelized xunit collection.

## 2026-07-14 — The rarity gate falls back to the slot's best variant on an invalid literal

The sentence-stage rarity gate (2026-07-12) compared candidates to the typed literal's frequency and was therefore disarmed when the literal was not a lexicon form — exactly the ca→çà residue: "ca" has no frequency, so çà (21/M) survived against ça (8 972/M), ratio 0.0024, four times under the 0.01 floor. The gate's reference now falls back to the slot's most frequent lexicon variant when the literal is invalid; personal variants never set the reference (their sentinel frequency would inflate it). Side effect, intended: a fold whose rare cousin drops can collapse to one form and stop being an ambiguous slot at all ("ete" → été alone; the commit stage already restores it deterministically).

## 2026-07-14 — Replay audit verdict: the 25% was a measurement artifact; the judge is rehabilitated

The contested replay agreement (~25%) that had condemned the Qwen judge was audited and reversed. Three measurement faults, found and repaired: the corpus final string was scored as if it were ground truth (it is what landed on screen, not what was meant); 290 legacy records carried no keystroke history and were unrepairable as recorded; elision fusion misaligned slots. Measured cascade after repair: 23% → 58% (measurement fixed) → 80.7% changes-only against the maintainer-filled truth sheet (241 truths). The margin curve, flat-to-noisy before the repair, is monotonic after it (87.6% @0.25 · 90.8% @0.5 · 100% @3.0 on the 979-slot pass). Known sheet limit, accepted: a filled truth on a slot where the judge stops contradicting the final disappears at regeneration.

## 2026-07-04 (later) — The sentence judge runs on the GPU (DirectML), measured against CPU int4

The Qwen3 closed-candidate judge now runs on the RX 7900 XT through a genai `-e dml` int4 export on the DmlExecutionProvider. CPU int4 and DirectML exports coexist under each model dir (`onnxruntime/cpu_and_mobile/…` and `onnxruntime/directml/directml-int4-block-32`); the provider is chosen in code and, for the bench, by `Deckle.Autocorrect.Probe --provider <cpu|dml>`.

Closed-candidate benchmark (30 cases, thresholds 0.25/0.5), summed per-case scoring time, CPU int4 vs DirectML:

| size | CPU int4 | DirectML | speedup | CPU @0.25 prec/recall/wrong | DML @0.25 prec/recall/wrong |
|------|---------:|---------:|--------:|-----------------------------|-----------------------------|
| 0.6B | 88.6s    | 18.0s    | ~4.9×   | 89% / 57% / 1               | 86% / 43% / 1               |
| 1.7B | 220.6s   | 19.2s    | ~11.5×  | 93% / 93% / 1               | 100% / 93% / 0              |
| 4B   | 470.1s   | 24.7s    | ~19.0×  | 93% / 93% / 1               | 82% / 64% / 2               |

Findings, measured:
- DirectML time is nearly flat across sizes (18→19→25s) while CPU scales with params (89→221→470s), so the GPU win grows with model size (≈5× → 19×). Per-case DML time is dominated by per-candidate generator creation, not the matmuls; DirectML compute-engine utilization sampled 4–34% during a run — engaged, far from saturated. On GPU, going from 0.6B to 4B costs almost nothing.
- 1.7B on DirectML is the cleanest operating point: 100% precision (zero false changes, vs the CPU int4's one on `ou_choice`) at 93% recall, in ~19s.
- The fp16 + accuracy_level-0 DML quantization is noisier at the extremes: 0.6B loses recall (57%→43%), and 4B makes two false literal-trap changes at margin 0.25 (both clean at 0.5). The CPU int4-kld export and the DML export are different quantizations — their margins are not interchangeable; the operating margin must be recalibrated per export.

Build recipe (onnxruntime-genai 0.13.0 model builder, Python 3.13 venv at `D:\models\_genai-build`):
`python -m onnxruntime_genai.models.builder -m Qwen/Qwen3-<size> -o <out> -p int4 -e dml -c <cache> --extra_options hf_token=false`
- `hf_token=false` is required to fetch ungated Qwen3 anonymously — without it the builder passes `token=True` and dies with `LocalTokenNotFoundError`.
- Beyond `onnxruntime-genai-directml==0.13.0` + torch (CPU is enough) + transformers + onnx, the builder needs `onnx_ir` — an undeclared import of both the builder and the int4 quantizer.
- No GPU is used at build time; RTN int4 quantization runs CPU-side.

Traps confirmed at the runtime:
- `-e dml` forces a FLOAT16 io dtype (there is no FP32 DML path), so the judge's logits come back float16; the scorer reads each row in its own element type.
- DirectML rejects continuous decoding — a second AppendTokens on a live generator throws "Continuous decoding is not supported on the selected device type". The scorer scores each candidate in a single forward.
- The CPU int4 export cannot run on DML: its accuracy_level-4 MatMulNBits nodes do not partition to DmlExecutionProvider, so session init fails ("graph capture … not all nodes partitioned"). A dedicated `-e dml` export (accuracy_level 0, fp16 io) is required — confirmed by loading each.
- `DirectML.dll` resolves from `System32` (v1.15.5); the genai NuGet does not bundle it.

Decisions taken without the maintainer (reversible, for review):
- Build route over download: no prebuilt small-Qwen3 DirectML genai export exists (onnx-community ships cpu/cuda/webgpu int4 for Qwen3 but no dml; only third-party 14B/32B DML builds exist). Built 0.6B/1.7B/4B locally with the builder defaults for `-e dml` (accuracy_level 0, block 32).
- Exports staged under `%LOCALAPPDATA%\Deckle\models\qwen3-<size>-onnx\onnxruntime\directml\directml-int4-block-32`, mirroring onnx-community's per-EP layout.
- `accuracy_level=1` (fp32 activation upcast, the onnx-community DML publishers' documented alternative) was not explored — a lever if the 0.6B/4B margin noise ever needs closing on GPU.

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

## 2026-07-21 — UIA caret ranges cross editor boundaries

Live probes in Codex and Anytype found a caret through TextPattern, but extending its degenerate range to the left returned at least 512 characters across the surrounding document rather than stopping at the composer or block boundary. Chose not to use that range to restore sentence context after pointer or navigation invalidation.

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
