---
name: interactive-autocorrect-research
description: "Reproducible campaign for safe, local, interactive whole-sentence autocorrection."
type: benchmark-campaign
module: benchmark/autoresearch/campaigns/interactive-autocorrect
---

# Interactive autocorrect research

## Objective

Improve Deckle's local autocorrection Pareto frontier without weakening these invariants:

- the exact literal sentence, punctuation, casing, and separators are the source of truth;
- every candidate is one bounded positional edit against that literal;
- one sentence close produces one global KEEP / one-edit / abstain decision;
- no verdict may change a different target, an active selection, a password surface, or text that has become stale;
- production autocorrect behavior does not change during the research campaign.

The product objective prioritizes precision among applied corrections over coverage, while rejecting the degenerate always-abstain solution. Latency is measured from physical terminal-punctuation key-down to the edit becoming observable on the target surface. The campaign studies 50, 100, and 150 ms as reference points, not elimination thresholds.

## Campaign state

- Worktree: `D:\worktrees\deckle\paragraph-correction-apply`
- Branch: `fix/paragraph-correction-apply`
- Starting HEAD: `5f8acc6a5f72db6934858d78128c4f984b381f6f`
- Compute owner: root orchestrator only
- Compute policy: one heavy task at a time
- Raw runs: `benchmark/runs/interactive-autocorrect/<experiment-id>/`
- Experiment record: [`experiment-log.jsonl`](experiment-log.jsonl), append-only
- Compute queue: [`compute-queue.jsonl`](compute-queue.jsonl), append-only events
- Result synthesis: [`RESULTS.md`](RESULTS.md)
- Architecture map: [`FAMILIES.md`](FAMILIES.md)

Raw runs are local, ignored, potentially private, and retained for the full campaign; a hash never substitutes for the raw artifact. They are never staged. Any later privacy purge requires Louis's explicit decision, is recorded as a retention event, and downgrades the affected run's reproducibility. Tracked campaign artifacts contain aggregate measurements, content-free metadata, and synthetic examples only.

## Established starting evidence

- Commit `1082aa9b` introduced exact closed-sentence global judgment, positional edits, active-selection rejection, and content-free outcome/reason logs.
- Commit `5f8acc6a` records a warm Qwen3-1.7B DirectML judgment at approximately 945 ms on the local machine.
- Verified-caret recovery has a configured 110 ms stable-read floor before judgment and performs another complete stable read before an edit is applied: a changing recovered verdict therefore pays at least 220 ms of configured sleeps, plus four UIA calls.
- The autocorrect test project previously passed 725/725; the full suite had seven reproducible out-of-scope failures involving Home and locked JSONL Diagnostics/Telemetry/Logging files.
- `Il y a une seul erreur.` is corrected by the current global transaction.
- `ok et donc Il y a une seul erreur.` abstains at margin 0.894 under the current 1.0 threshold.

These are imported findings, not measurements made by this campaign. They must not be treated as a new baseline until an experiment reproduces them with the schema below.

## Initial technical options

1. Optimize the terminal Qwen path: reuse generators/state, reduce or approximate order normalization, quantize, cache, and profile candidate scoring.
2. Compute speculatively while typing and validate only the exact terminal state at closure.
3. Use a deterministic-rule / compact-discriminator fast path with a slower Qwen teacher or shadow path.
4. Reduce integration cost through pre-anchored UIA, per-surface strategies, or a later TSF investigation.
5. Keep Qwen off the direct interaction path and use delayed or explicit suggestions where fast paths abstain.

Initial recommendation: center the first laboratory on options 2 and 3, measure options 1 and 4 as independent axes, and retain option 5 as a safety baseline. This recommendation is provisional and may be replaced by experimental evidence.

## Experimental contract

Every experiment record must include:

- `experiment_id`, hypothesis, baseline, decision criterion, budget, exact command, HEAD, configuration, data identifiers and split, seed where relevant, warm/cold state, raw-output paths, wall duration, validity, and decision;
- counts sufficient to recompute precision, generator coverage, decider coverage, final coverage, useful/regrettable abstention, integrity failures, and stale result outcomes;
- raw latency observations sufficient to recompute p50, p95, p99, and maximum;
- decision-ready lead time before terminal punctuation when speculative work is involved;
- CPU, GPU, and memory observations when the method performs per-keystroke or resident work;
- application and surface identity for integration measurements.

A technical failure is recorded as `invalid`, never as evidence against a technical family. Candidate ordering is randomized or inverted where an LLM judge is involved, and unstable qualitative judgments are invalidated.

## Data discipline

Use three levels: visible development/synthetic cases, sparse consulted selection validation, and a sealed final holdout. Parent sentences, close variants, and associated punctuation stay in one split. Physical captures made by Louis form a separate temporal test and never leak into tuning data.

The Pareto view keeps non-dominated alternatives across applied precision, coverage, latency, integrity, resource cost, and compatibility. No scalar score is allowed to hide an integrity failure.
