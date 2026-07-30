# Architecture family map

This map is an orchestrator synthesis, not an agent vote. A status changes only
when a reproducible experiment or an established repository fact justifies it.

Status vocabulary: **baseline**, **active**, **candidate**, **dormant**, and
**refuted as scoped**. A scoped refutation preserves adjacent uses.

## Established constraints

- The semantic input is one exact UTF-16 literal plus at most twelve positional
  one-edit candidates. One closure grants one global KEEP/edit/abstain decision.
- ACX-0003 proves that invalidated work retains single-flight ownership and blocks
  fresh work. Epoch rejection itself is established by separate code/tests, not
  by that experiment's abstaining held verdict.
- Modifier-only physical transitions currently cross the application boundary;
  a held Shift allowed one simulated injector call (ACX-0004).
- The observed pre-terminal gap is 353 ms p50. Under a punctuation oracle, a
  945 ms decision was ready before only 13.86% of usable terminal gestures,
  whereas 150 ms reached 95.18% with an immediate trigger (ACX-0005).
- Recovered UIA application carries at least 220 ms of configured stable-read
  sleeps for a changing verdict, before UIA call cost, injection, and rendering.

## Recommended shape (interpretation)

1. A **transaction builder** owns the exact literal, ordered positional edits,
   and complete artifact/configuration fingerprint.
2. **Semantic deciders and caches** produce inert evidence. They never acquire
   write authority.
3. An **application lease** is minted only from punctuation-time state: engine
   generation, epoch, exact transaction equality, target identity, enrollment,
   protected/read-only/selection gates, neutral modifiers and IME, exact current
   suffix, and lifecycle state.
4. An **integration profile** applies one submitted edit through a proven surface
   path and records the observed exact postcondition. SendInput acceptance alone
   is not the postcondition.
5. **Slow/shadow work** has separate ownership and cannot block the fast lane.

This preserves a minority possibility: TSF or a control-native range path may
become the high-trust profile even if UIA plus SendInput remains a broad fallback.

## Family status

| Family | Status | Evidence / next discriminator |
|---|---|---|
| Deterministic commit/candidate preparation | baseline | ACX-0001/0002: 37/37 internal original/replacement pairs match the visible synthetic gold, 92.5% recall, 2.3 us p50 / 86.6 us p99 managed commit cost. Positional precision, applied precision, and field quality are unmeasured. |
| Terminal Qwen3-1.7B DML as-is | refuted as scoped | About 945 ms warm before integration is outside direct interaction. ACX-0005 separately gives a fixed 945 ms computation 13.86% readiness on a selected timing sample. Teacher, shadow, batching, and caching survive. |
| Speculative Qwen on the production lane | refuted as scoped | ACX-0003 shows obsolete single-flight work blocks fresh work. It survives only with measured exact hits and separate ownership. |
| Exact anticipation with a fast decider | active | ACX-0005 shows a large 50–150 ms opportunity. Next: filter to exact candidate-eligible transactions and measure branch waste. |
| Batched/reused Qwen scoring | active | Current scorer runs both orders and one generator/forward per candidate. Next: candidate-count and stage profile before a batch prototype. |
| Deterministic unanimous global rules | candidate | Existing global `la/là` rule proves the contract shape, not residual coverage. Freeze a claim inventory on development data. |
| Compact tabular discriminator | candidate | Best low-data baseline. Score KEEP plus every edit over grouped transaction splits. |
| Compact neural global discriminator | candidate | Plausible 10–100 ms class, but trusted labels need audit. Start only if rules/tabular plateau. |
| Pointwise reranker | candidate baseline | Cheap reference; compare at identical features/capacity because it may mis-handle candidate competition. |
| Pairwise edit-vs-KEEP | candidate | Encodes asymmetric harm; insufficient alone for competing edits. Useful baseline/auxiliary loss. |
| Listwise KEEP-plus-edits | candidate, recommended learned contract | Closest match to one global decision. Require permutation-stable verdicts. |
| Setwise candidate interaction | dormant | Run only if multi-edit transactions expose a measured listwise deficit. Higher overfit risk. |
| Calibration/OOD/family thresholds | candidate | Global calibration first; conditional thresholds only if gains survive grouped validation. |
| Fast rules/model plus Qwen shadow | candidate, recommended cascade | One interactive decision; Qwen owns no application token. Exact fingerprints and separate lanes are mandatory. |
| Qwen teacher distillation | dormant | Teacher logits may help training but never replace human truth or touch validation/holdout. |
| Exact transaction cache | candidate | Full-record equality after hash hit plus separate application lease. Measure hit rate before implementation. |
| Compact-model quantization | dormant | Quantize only a validated finalist; recalibrate and require target-machine gain. |
| UIA pre-anchor + SendInput + postcondition | active integration baseline | Broad, but precheck is not atomic and postcheck detects damage after the fact. |
| Win32 Edit/RichEdit or TOM range path | candidate | Narrow but potentially fast and range-directed. Compare exact delta, focus, undo, Unicode, read-only, and UIPI. |
| TSF range-owned edit | dormant high-trust candidate | Strong potential edit-session/IME semantics; high COM/deployment cost and unknown coverage. |
| Per-surface capability routing | candidate | Key by app/version/control signature/capability, not process name alone; fail closed on drift. |
| Blinded personal delay study | blocked on voluntary physical session | Playground must hide/randomize delays and retain actual timer overshoot and visible-change endpoints. |

## Falsifiable experiment cards

### FAM-01 — Qwen candidate-count and stage profile

- **Hypothesis:** repeated generator/forward setup and prompt growth explain enough
  of 945 ms that batching can change the latency class.
- **Experiment:** randomized warm blocks at 2/4/8/13 candidates; decompose render,
  tokenize, generator construction, forward, logits copy/upcast, log-softmax, and
  disposal; compare scores and decisions exactly.
- **Cost:** medium native compute and instrumentation.
- **Risks:** thermal drift, DirectML contention, token-count confounding.
- **Falsifier:** forward dominates or realistic 13-candidate p95 stays above 300 ms
  after amortizable work is removed.

### FAM-02 — candidate-eligible anticipation oracle

- **Hypothesis:** a 50–150 ms global decider is ready before punctuation for at
  least 80% of transactions that actually contain a generated edit.
- **Experiment:** replay the consented stream through the transaction builder
  without applying; join exact transactions to punctuation gaps and branch hits.
- **Cost:** medium harness work, low compute.
- **Risks:** replay/surface drift, repair ambiguity, accidental text output.
- **Falsifier:** eligible readiness below 50%, exact-branch hit below 25%, or more
  than three speculative runs per useful hit.

### FAM-03 — deterministic unanimous global rules

- **Hypothesis:** frozen high-anchor rules cover at least 5% of residual generated
  opportunities below 1 ms p95 with zero sealed false applications.
- **Experiment:** rules emit claims on submitted edits; apply only when every
  non-null claim agrees. Compare against KEEP and the generator oracle.
- **Cost:** low to medium.
- **Risks:** lexical memorization, rule growth, hidden priority order.
- **Falsifier:** any sealed false application or less than 3% useful coverage.

### FAM-04 — compact global baseline

- **Hypothesis:** a tabular/listwise model reaches Qwen-like selective quality at
  useful coverage below 2 ms p95.
- **Experiment:** grouped splits; equal-budget pointwise/pairwise/listwise objectives;
  KEEP and abstention separate; risk-coverage frozen before holdout.
- **Cost:** medium adjudication, low training/runtime.
- **Risks:** truth-selection bias, clustering, frequency and order leakage.
- **Falsifier:** it cannot reach the Qwen reference precision at half its coverage.

### FAM-05 — synthetic-delay integration intercept

- **Hypothesis:** end-to-end latency is approximately integration intercept plus
  decision delay, and the intercept dominates a sub-100 ms judge.
- **Experiment:** deterministic correction with delays 0/25/50/100/250/500/1000 ms
  through isolated cross-process controls; compare SendInput and UIA validation.
- **Cost:** medium Playground instrumentation, no model.
- **Risks:** observation perturbation and an invalid visible endpoint.
- **Falsifier:** latency is not monotonic with inserted delay.

### FAM-06 — control-native range edit

- **Hypothesis:** Edit/RichEdit messages or TOM produce a faster, more exact delta
  than SendInput on proven control families.
- **Experiment:** identical ranges with focus races, selection, read-only, undo,
  formatting, emoji, combining sequences, and UIPI.
- **Cost:** medium.
- **Risks:** subclassed controls, selection race, profile drift.
- **Falsifier:** no integrity advantage or damaged undo/selection/formatting.

### FAM-07 — minimal TSF capability prototype

- **Hypothesis:** a TSF read/write edit session eliminates wrong-target and deletion
  failures on enough priority surfaces to justify native complexity.
- **Experiment:** one range-owned prototype against the same focus, selection,
  Unicode, IME, undo, and latency matrix as UIA/SendInput.
- **Cost:** high.
- **Risks:** C++/COM lifecycle, registration/signing, limited coverage.
- **Falsifier:** insufficient coverage or no measured integrity/latency advantage.

### FAM-08 — masked personal delay threshold

- **Hypothesis:** disruption stays below 5% through 100 ms and rises by 150 ms.
- **Experiment:** balanced hidden delays with catch/sham trials; separate 2AFC
  detection from continuation typing; retain next-key interference and visible time.
- **Cost:** 45–60 minutes of voluntary physical testing.
- **Risks:** one informed participant, fatigue, salience and learning.
- **Falsifier:** flat curve, threshold below 100 ms, or high catch false reports.
