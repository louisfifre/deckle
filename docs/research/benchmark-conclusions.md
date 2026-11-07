# What the benchmarks and offline probes concluded

The `benchmark/` workspace, `src/Deckle.Autocorrect.Probe` and `src/Deckle.Autocorrect.Mlm` are removed (branch `chore/remove-benchmarks`). A bench is cheap to rewrite: a prompt, a corpus, a judge model beside it. This note keeps what each one asked and what is still worth knowing. It is written from the READMEs, the commit bodies and the headers of the removed code.

**Limit of this record.** Raw runs, corpora and result tables were local and never versioned. For most campaigns no result table survives in the repository, so only the figures quoted below are established. Anything else would be a guess. The removed code stays reachable in git history (last commit before the removal: the parent of the commit that deletes `benchmark/`).

No corpus text is quoted here, on purpose: the corpora held the maintainer's own dictations and typing.

## Sentence judge (Qwen3-1.7B, ONNX GenAI, DirectML int4)

Live code: `Deckle.Autocorrect.Onnx`, margin set in `src/Deckle.App/App.Autocorrect.cs` (`SentenceJudgeMargin = 1.0`).

**Margin calibration (replay, 2026-07).** Question: at what score gap between the best and the second candidate should the judge act? Method: replay a corpus of typed sentences, judge the ambiguous slots with the final sentence as context, collect results at margin 0, then sweep the threshold.

| Margin | Precision | Coverage |
|---|---|---|
| 0.5 | 90.8 % | 41.0 % |
| 1.0 | 92.2 % | 20.8 % |

Decision taken: margin 1.0, precision first (an abstention is invisible, a wrong change erodes trust), to be relaxed as the corpus widens. The margin is per export. It was measured on the DML int4 export and must be recalibrated if the export, the prompt features (BOS token, suffix evidence) or the execution provider change. The loader already logs when a calibrated prompt feature is missing for that reason.

**Execution provider.** DirectML loads the CPU int4 exports (kld-block-128 MatMul) but cannot run them: their nodes do not partition to the DML provider. The GPU path needs a `model-builder -e dml` export. onnxruntime-genai DirectML is pinned to 0.13.0 (0.14 had an ORT API and bundle mismatch). A CPU provider exists as a fallback.

**Latency.** About 0.6 s per verdict at the time the judge went live (commit message), about 945 ms for a warm judgment in the later interactive-autocorrect campaign. Both are far above the 50, 100 and 150 ms references that campaign studied, which is why the judge runs on the background rerank lane and its verdicts drop by epoch when stale.

**Sentence-stage probes (Probe: profile, calibration, canonical latency, order ablation, batch experiment, unanimity bundle, decision inventory).** They asked how much of the judge's latency comes from candidate count (2, 4, 8, 13), call order, batching candidates, and whether a cheap deterministic rule set agreeing with the judge could stand in for it (unanimity of morphological rules, reported against a 1 ms warm p95 reference). The harness used Latin-square candidate rotations, fixed seeds, warm-up blocks and 300 ms p95 reference points. No result table was kept. Whether any of them changed the live path is not recorded.

## Interactive autocorrect campaign (autoresearch)

Question: improve the precision / coverage / latency frontier of whole-sentence autocorrection without weakening integrity. What the campaign established as a contract, still useful if it restarts:

- Integrity gate first, zero tolerance: wrong target, stale state, active selection, password or unknown surface, more than one write per sentence close, write after stop or focus loss, an observed delta that is not exactly one positional edit. A scalar score never compensates for a failure.
- Precision of applied corrections is measured only after the target mutation is independently observed. No-change results report precision as not measured, never 100 %.
- Latency endpoint is the observed change on the target, not `SendInput` acceptance. Cold, warm and idle-cooled states are never pooled.
- The 99.9 % precision ambition needs about 2,995 independent applied corrections with zero errors (one-sided 95 % exact binomial); clustered personal data needs more.
- Imported starting facts: verified-caret recovery waits a 110 ms stable read before judgment and again before applying, so a changing verdict pays at least 220 ms of sleeps plus four UIA calls. The autocorrect test project passed 725 of 725 at the time.
- Options never settled: speculative scoring while typing (the lead-time oracle was an upper bound, not a proof), a deterministic fast path with the judge as slower teacher, per-surface UIA strategies, delayed or explicit suggestions.

Probes built for it: anticipation lead oracle and transaction join (timing around terminal punctuation only, no text kept), stale single-flight work (can a newly eligible sentence reach the judge while an invalidated judgment holds the lane), caret-context probe. The live lane does drop stale verdicts by epoch; the probe's own result was not kept, so a link between the two is not established.

## Qwen adapter compatibility (ACX-0023)

Question: can a LoRA-style adapter (rank-bounded, query and value projections) be applied to the Qwen3-0.6B ONNX graph on CPU, with the base revision, graph hash and tensor shapes pinned, and do runtime negatives (a wrong shape, a wrong hash) fail closed? It was a protocol and gate exercise (manifest gate, artifact gate, resource sampler at 50 ms). No adapter was shipped and no live code depends on it.

## Closed correction benchmark and deterministic replay

Two gold corpora replayed real keyboard events through `AutocorrectEngine` with only the OS ports faked: a product corpus shared with the quality gate in the tests, and a domain-pack corpus (fr-it) that checks pack literals stay protected, sole-candidate accent restoration fires, and typo repair clears the frequency bars. Findings that survive in live code: a margin is not safety when the candidate is morphologically impossible in context, and a valid pack literal must be protected whatever its frequency. The live tests under `tests/Deckle.Autocorrect.Tests` keep the regression cases that matter.

## CamemBERT masked-LM reranker (`Deckle.Autocorrect.Mlm`, offline probe)

Question: can a masked-LM choose the right accented form among a closed set (a / à, ou / où, la / là, du / dû, sur / sûr...), from bidirectional context, on CPU with no Python? Method: balanced cases per surface form, mask the slot, rank by fill-mask logit plus a frequency prior, report forced-choice accuracy and a confidence / coverage curve.

Conclusion kept: it only handles single-piece function-word slots (multi-piece forms such as côté / coté would need per-subtoken PLL scoring) and cannot satisfy the whole-sentence decision contract, so the Qwen sentence judge replaced it and it never stands in when the judge is missing. The setup assistant no longer stages it. No accuracy table survives.

## Paragraph rewrite gate (`rewrite/paragraph-gate`)

Question: does a Ministral-class local model at temperature 0, behind the production `ParagraphRewrite` prompt, produce rewrites that `RewriteDiffGate` accepts, at a latency compatible with offering the result?

Measured on 16 typed-style samples: with `ministral-3:14b`, 11 offers and 1 identity out of 16, p50 about 650 ms warm, and every rejection was a caught hallucination. `ministral-3:3b` was below the capability floor. Gate calibration that came out of it and is pinned by tests in `Deckle.Llm.Rewrite`: three false-accept classes closed (markdown decoration passing as punctuation, group dilution around a verbatim word, francization), relative bound tightened from 34 % to 25 %. The gate's constants are code-level, never settings.

## Whole-dictation rewrite, model compare (`rewrite/model-compare`)

Question: which Claude model and which wording of the Lissage system prompt best keep a dictation's meaning when the profile rewrites it whole? Method: a model by prompt matrix on real dictations, a stronger reasoning call (effort high) listing meaning changes per rewrite (term substitutions, meaning inversions, dropped hedges, omissions, additions), plus deterministic marker deltas (negations with and without « ne », hedges per 100 words).

No result table was kept, so no model ranking is recorded here. What is established and worth keeping when it is rebuilt:

- The judge is a lead, not ground truth: read the flagged spans before trusting a count.
- Marker deltas are blunt. Restoring a dropped « ne » is normal cleaning, so watch the negation column without « ne ».
- A prompt snapshot can drift from the live profile: the production rewrites carried a prompt hash that no longer matched the live profile when the study ran, so rows of different origin did not share a prompt. Always hash the prompt per row.
- Generations should go through the production engine (`ClaudeEngine`, effort low) and every call spends real money, so print costs per line.

## Earlier benches already gone

Older entries in `CHANGELOG.md` (ASR, TTS audition with Chatterbox and Orpheus, the first prompt autoresearch loop) describe benches removed before this one. Their conclusions live in the changelog only.
