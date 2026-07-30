---
name: interactive-autocorrect-protocol
description: "Metric, latency, integrity, and data contract for the interactive autocorrect campaign."
type: benchmark-protocol
module: benchmark/autoresearch/campaigns/interactive-autocorrect
---

# Campaign protocol

## Two independent scorecards

An architecture enters the linguistic Pareto frontier only after passing the integrity gate. No weighted score can compensate for an integrity failure.

### Integrity gate — zero-tolerance counts

- wrong-target, stale-state, active-selection, sensitive/unknown-password, read-only, non-prose, or non-enrolled writes;
- observed before/after delta different from exactly one submitted positional edit;
- more than one sentence-stage write per closure;
- writes after stop, disable, dispose, focus loss, navigation, opaque mutation, or failed injection;
- accepted `SendInput` bursts whose observed postcondition is missing or different;
- captured or persisted sensitive text;
- internal model divergence after a nominally successful edit.

Every counter is reported separately and per application/surface. The acceptable count is zero.

### Linguistic scorecard

For each frozen, adjudicated event `i`:

- `L_i`: exact literal sentence;
- `T_i`: set of acceptable exact final sentences;
- `O_i`: the literal is wrong and a permitted bounded edit can reach `T_i`;
- `C_i`: complete generated set, including the literal;
- `G_i`: generated truth, `C_i` intersects `T_i`;
- `Q_i`: transaction reached a judge;
- `D_i`: the judge selected a correct nonliteral candidate;
- `Z_i`: explicit abstention; KEEP is separate;
- `M_i`: a Deckle-attributable target mutation was observed;
- `S_i`: the intended unchanged target was mutated exactly to a member of `T_i`.

Report trigger coverage, generator coverage, judge action rate, judge correct coverage, decision precision, final coverage, applied-correction precision, useful abstention, and regrettable abstention. Candidate miss, KEEP on wrong literal, low margin, timeout, inference error, invalidation, injection refusal, accepted-but-unobserved injection, and candidate overflow remain distinct terminal reasons.

No-change results report precision as not measured, never as 100%. Always-abstain and literal-only KEEP remain explicit baselines.

## Latency contract

Engineering latency is `WM_INPUT receipt -> observed semantic target mutation`. The product claim remains `physical switch closure -> first visible corrected frame`; it requires an external same-clock observation. `SendInput` acceptance and `CorrectionApplied` are not observed-change endpoints.

Report raw samples, count, observation window, p50, p95, p99, and maximum per surface and warm-state stratum. Do not pool process/model cold, first provider execution, steady active warm, idle-cooled, UIA-cold, UIA-warm, target-cold, or target-warm observations.

For speculative work:

`decision_ready_lead_ms = terminal_key_raw_ms - decision_ready_ms`

Positive values mean the decision existed before punctuation. Also record `safe_apply_ready_ms` after exact terminal validation. A speculative hit is valid only when its exact literal, candidate set, model/config, order policy, epoch, and target anchor match the terminal transaction.

## Data contract

- Freeze inventory membership before model prediction or truth review.
- Keep parent sentences, close variants, punctuation variants, shared candidate families, and source sessions in one split.
- Use visible development data, sparse selection validation, and a sealed final holdout.
- Preserve a forward-time evaluation and Louis's morning physical captures as separate temporal tests.
- Truth may contain several acceptable exact targets or `unresolved`.
- Synthetic safety stress sets retain veto power but are never prevalence-weighted into representative quality.
- Every attempted configuration increments the validation-query count; the sealed holdout is reserved for a fixed finalist set.

The 99.9% precision target is an ambition. With zero observed errors, approximately 2,995 independent applied corrections are needed for a one-sided 95% exact-binomial lower bound of 99.9%; clustered personal data requires more evidence and cluster-aware intervals.

## Reproduction contract

- A clean experiment records the exact Git HEAD and hashes every executed assembly and raw output.
- A dirty-source experiment is exploratory unless its complete diff bundle, untracked-source bundle, and executed-assembly hashes are preserved. A prose dirty-state description is insufficient.
- Growing private inputs are replayed by an exact byte prefix or immutable snapshot. The record includes the prefix length, SHA-256, line-boundary status, and whether the source changed while it was read.
- Later audit corrections are appended as new JSONL events. Historical records and raw artifacts are never rewritten to make an experiment appear cleaner.
- Pair-only internal expected-output agreement is named `internal_edit_pair_precision`; it is not positional precision. `applied_correction_precision` exists only after the intended target mutation is independently observed.

## First experiment sequence

1. `ACX-0001`: reproduce the existing deterministic quality/cost baseline and validate raw-artifact capture.
2. Exact-transaction property oracle over Unicode, repeated words, separator runs, punctuation, and candidate positions.
3. Deterministic event-order probe for stale work, modifier changes, lifecycle transitions, and head-of-line blocking.
4. Add stage-decomposed, machine-readable campaign output without changing production logging or behavior.
5. Establish a deterministic isolated-control latency oracle in Playground before any external application phase.

Only then begin terminal-Qwen profiling, speculative lead-time work, compact discriminators, and surface-integration comparisons.
