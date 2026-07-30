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
- ACX-0019 exactly reconstructs 54 positional edits across the 35 public
  development cases. The existing global locative rule makes one correct edit,
  no wrong edit, and abstains on the other 34 cases at 0.0003 ms warm p95.

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
| Deterministic commit/candidate preparation | reproducible baseline | ACX-0006 on clean HEAD: 37/37 internal original/replacement pairs match visible synthetic gold, 92.5% recall, 2.2 us p50 / 83.9 us p99 managed commit cost. Positional precision, applied precision, and field quality are unmeasured. |
| Terminal Qwen3-1.7B DML as-is | refuted as scoped | ACX-0011 canonical two-candidate p50/p95 is 907.496/935.752 ms before integration. ACX-0005 separately gives a fixed 945 ms computation 13.86% readiness on a selected timing sample. Teacher, shadow, coarse ablation, batching, and caching survive. |
| Forward-only Qwen scoring | research candidate; measured direct condition refuted | ACX-0012 fixed canonical two-candidate continuous-hot mixed-method p50/p95 is 393.180/537.587 ms. The relative-latency gate passed, but the 300 ms reference failed and `literal_ratures` remained a wrong internal selection. Shadow use is viable; anticipation is unrefuted but unestablished pending a joined eligibility, lead-time, lease, integration, and observed-target experiment. |
| Reverse-only Qwen scoring | dominated by forward on measured evidence only | ACX-0012 reverse-only was slower than forward and worse on frozen visible internal-decision metrics. Combined was slower but linguistically better than reverse, so no universal dominance is claimed. |
| Speculative Qwen on the production lane | refuted as scoped | ACX-0007 directly shows changing stale verdicts are rejected but obsolete single-flight work still blocks fresh work. It survives only with measured exact hits and separate ownership. |
| Exact anticipation with a fast decider | reproducible replay baseline; integration active | ACX-0014 preserves 196 independently safe exact transactions after quarantining three boundary disagreements. Immediate 150 ms readiness is 186/196 (94.90%); a fixed dot branch hits 151/196 (77.04%), while four branches waste three jobs per hit. Next: measure real incremental preparation, scheduler ownership, lease safety, and observed visible latency. |
| Batched/reused Qwen scoring | dormant for direct interaction | ACX-0018 established exact two-row DirectML batch construction and finite logits, but forward scores diverged by 0.0156224 against the frozen 0.001 tolerance. The semantic prerequisite failed, so latency is ineligible. Preserve batching only for separately preregistered diagnostics, shadow/teacher work, or a future numerical-kernel investigation; do not relax the contract after observation. |
| In-process Stopwatch stage attribution | refuted as scoped | Two designs preserved semantics but could not separate collector cost from a 140 ms immediate-repeat/position effect; ACX-0010 upper bounds were 65.097 ms and 8.562%. External GPU/ORT tracing remains a separate family. |
| Deterministic unanimous global rules | active from reproducible one-rule baseline | ACX-0019 freezes the public inventory: 1 correct edit, 0 wrong edits, 19 useful and 15 regrettable abstentions, 0 KEEP; all 66 edit orders are identity-stable at 0.0003 ms warm p95. ACX-0020 tests a precedence-free unanimity bundle with at least two correct residual edits across two families. |
| Compact tabular discriminator | candidate | Best low-data baseline. Score KEEP plus every edit over grouped transaction splits. |
| Compact neural global discriminator | candidate | Plausible 10–100 ms class, but trusted labels need audit. Start only if rules/tabular plateau. |
| Pointwise reranker | candidate baseline | Cheap reference; compare at identical features/capacity because it may mis-handle candidate competition. |
| Pairwise edit-vs-KEEP | candidate | Encodes asymmetric harm; insufficient alone for competing edits. Useful baseline/auxiliary loss. |
| Listwise KEEP-plus-edits | candidate, recommended learned contract | Closest match to one global decision. Require permutation-stable verdicts. |
| Setwise candidate interaction | dormant | Run only if multi-edit transactions expose a measured listwise deficit. Higher overfit risk. |
| Calibration/OOD/family thresholds | candidate | Global calibration first; conditional thresholds only if gains survive grouped validation. |
| Fast rules/model plus Qwen shadow | candidate, recommended cascade | One interactive decision; Qwen owns no application token. Exact fingerprints and separate lanes are mandatory. |
| One Qwen base plus task LoRA adapters | candidate | The pinned ORT GenAI 0.13.0 managed assembly exposes `Adapters.LoadAdapter` and `Generator.SetActiveAdapter`, and upstream documents Multi-LoRA. ACX-0022 must test one shared Qwen export with at least two task adapters, on-disk deduplication, switch latency, RAM/VRAM residency, exact output isolation, and DirectML compatibility before any shared-model claim. |
| Qwen teacher distillation | dormant | Teacher logits may help training but never replace human truth or touch validation/holdout. |
| Exact transaction cache | candidate | Full-record equality after hash hit plus separate application lease. Measure hit rate before implementation. |
| Compact-model quantization | dormant | Quantize only a validated finalist; recalibrate and require target-machine gain. |
| UIA pre-anchor + SendInput + postcondition | active integration baseline | Broad, but precheck is not atomic and postcheck detects damage after the fact. |
| Delayed closed-sentence range-owned correction | candidate | Correcting sentence A while typing sentence B must never replay Backspace or replacement text at the current caret. ACX-0021 will compare range-owned Edit/RichEdit/TOM and TSF prototypes under concurrent continuation typing, exact reread/fingerprint gates, caret/selection/undo preservation, and exact postcondition verification. |
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
- **Current evidence:** ACX-0009 exact-equivalence and visible-quality results are
  valid, but stage attribution is invalid because the observer gate failed and
  the apparent delta is confounded with first/second call position. ACX-0010
  rejected the calibrated in-process profiler, ACX-0011 established the
  production-canonical whole-call baseline, and ACX-0012 showed a valid roughly
  half-cost forward-only boundary that still missed 300 ms and retained a wrong
  internal selection. ACX-0018 later established native exact-row batch
  feasibility but refuted numerical equivalence under the frozen forward score
  contract on its first eligible fixture. Because semantics gated timing, no
  batching speedup is established. Direct interactive batching is dormant;
  shadow, teacher, and separately preregistered numerical diagnostics survive.

### FAM-02 — candidate-eligible anticipation oracle

- **Hypothesis:** a 50–150 ms global decider is ready before punctuation for at
  least 80% of transactions that actually contain a generated edit.
- **Experiment:** replay the consented stream through the transaction builder
  without applying; join exact transactions to punctuation gaps and branch hits.
- **Cost:** medium harness work, low compute.
- **Risks:** replay/surface drift, repair ambiguity, accidental text output.
- **Falsifier:** eligible readiness below 50%, exact-branch hit below 25%, or more
  than three speculative runs per useful hit.
- **Current evidence:** ACX-0014 passed the predeclared replay gates on 196 safe
  exact transactions: immediate 150 ms readiness 94.90%, fixed-dot hit 77.04%,
  and four-terminal structural waste exactly three jobs per hit. Three additional
  captures were quarantined because production state and independent raw-stream
  boundary evidence disagreed after the last observed terminal was erased. The
  remaining unknowns are real pre-terminal preparation, scheduler contention,
  application lease and observed visible mutation; no generator-coverage claim
  follows from the replay.

### FAM-03 — deterministic unanimous global rules

- **Hypothesis:** frozen high-anchor rules cover at least 5% of residual generated
  opportunities below 1 ms p95 with zero sealed false applications.
- **Experiment:** rules emit claims on submitted edits; apply only when every
  non-null claim agrees. Compare against KEEP and the generator oracle.
- **Cost:** low to medium.
- **Risks:** lexical memorization, rule growth, hidden priority order.
- **Falsifier:** any sealed false application or less than 3% useful coverage.
- **Current evidence:** ACX-0019 establishes the one-rule public-development
  baseline, not the bundle hypothesis: 54 edits reconstruct exactly, all 66
  edit orders preserve identity, and the locative rule selects the one correct
  `la` → `là` edit with no wrong decision. It emits no affirmative KEEP and
  leaves 15/16 correctable cases as regrettable abstentions. The 10,000-sample
  warm p95 is 0.0003 ms. ACX-0020 is selected to test at least two residual
  edits across two frozen families under rule-order unanimity.

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

### FAM-09 — delayed range-owned closed-sentence correction

- **Hypothesis:** a completed sentence can be corrected after continuation typing
  has begun without moving or using the current caret, changing any unrelated
  UTF-16 unit, disturbing selection/IME state, or corrupting undo.
- **Experiment:** in the Playground, close sentence A, begin sentence B, then
  release a delayed positional edit into A through control-native range APIs and
  a minimal TSF prototype. Exercise target drift, focus changes, read-only state,
  emoji, combining sequences, IME composition, selection, and rapid typing.
- **Cost:** medium to high integration work; no model required initially.
- **Risks:** controls without stable range identity, undo fragmentation, race
  between reread and commit, and surface-specific behavior.
- **Falsifier:** any character lands at the live caret, any undeclared text unit
  changes, caret/selection/IME/undo cannot be preserved, or stale target/range
  state is accepted. UIA plus current-caret `SendInput` is ineligible by design.

### FAM-10 — one shared Qwen base with task adapters

- **Hypothesis:** one local Qwen base can serve multiple Deckle LLM tasks through
  separately trained LoRA adapters while storing and loading the base only once,
  with materially lower disk usage than one full model per task and acceptable
  adapter-switch latency and memory residency.
- **Experiment:** export two deliberately distinct adapters against the exact same
  base and target-layer contract; load both through pinned ORT GenAI 0.13.0,
  alternate them over seeded prompts, and retain base/adapter file sizes, cold and
  warm load/switch samples, RAM/VRAM deltas, output fingerprints, and isolation
  checks. Run CPU technical smoke before a serial DirectML probe.
- **Cost:** medium export/runtime work plus later task-specific training data.
- **Risks:** adapter export/provider incompatibility, identical-layer constraint,
  GPU copies per adapter, quality interference, preview API churn, and base-model
  capability limits that an adapter cannot repair.
- **Falsifier:** adapters require duplicated base artifacts or model sessions,
  cannot switch reliably on DirectML, leak state across tasks, or save too little
  disk/memory relative to their latency and quality cost.
- **Current evidence:** the local 0.13.0 managed assembly exposes
  `Adapters.LoadAdapter`, `Adapters.UnloadAdapter`, and
  `Generator.SetActiveAdapter`; ONNX Runtime GenAI documents Multi-LoRA and
  `.onnx_adapter` files. No Deckle Qwen adapter has yet been exported, loaded,
  timed, or quality-tested.
