---
name: interactive-autocorrect-results
description: "Evidence synthesis and provisional Pareto frontier for the interactive autocorrect campaign."
type: benchmark-report
module: benchmark/autoresearch/campaigns/interactive-autocorrect
---

# Results

The first campaign baseline is valid for deterministic regression and managed commit cost only. It is not an estimate of field precision or visible interaction latency.

## Baselines

| ID | Pipeline | Internal edit-pair precision | Recall | Exact scenarios | Managed latency p50 / p95 / p99 / max | Scope |
|---|---|---:|---:|---:|---:|---|
| ACX-0001 | Deterministic commit + candidate preparation, no model | 37/37 (100% point estimate); positional and applied precision N/A | 37/40 (92.5%) | 20/23 (87.0%) | 2.5 / 82.6 / N/A / 11,074.2 us | Visible synthetic corpus, simulated OS ports |
| ACX-0002 | Same pipeline, machine-readable samples + exact-transaction probes | 37/37 (100% point estimate); positional and applied precision N/A | 37/40 (92.5%) | 20/23 (87.0%) | 2.3 / 67.7 / 86.6 / 12,077.3 us | Visible synthetic corpus, simulated OS ports; 25/25 focused contract tests; dirty-source reproduction pending |
| ACX-0006 | Clean committed reproduction of the machine-readable baseline | 37/37 (100% point estimate); positional and applied precision N/A | 37/40 (92.5%) | 20/23 (87.0%) | 2.2 / 65.7 / 83.9 / 13,727.1 us | HEAD `27f3fcb0`, assembly and raw outputs fingerprinted; counts and percentiles reconciled |

Raw output: `benchmark/runs/interactive-autocorrect/ACX-0001/probe-output.txt` (SHA-256 `346259B234C8DC56AF775B35ABDCCA26F838701396A7A8009F6004A8D5E0125A`).

The maximum is about 134 times p95. The command does not emit p99, so no p99 is inferred.

ACX-0002 raw output: `benchmark/runs/interactive-autocorrect/ACX-0002/probe-output.json` (SHA-256 `950346FC88EF3EEB31E8F5854498A934EFEA6910D13E048A88D597C471A49B20`). It retains 16,700 commit samples; independently recomputed p99 is 86.6 us and independently summed candidate generation is 8,269, matching the report.

ACX-0006 closes the source-state gap. Its raw output SHA-256 is `288B69BB0AC2E4EF12B24E9025AA26F699B2FB49B38B394D91C77FD2613215D2`; the executed probe assembly is `8C32C04AE4C588B2E1D5EED05ED12CCC2C0FF537454B87C6F8D54295AB757F40`. All 16,700 commit samples, 167 candidate samples, 8,269 generated candidates, and four reported latency order statistics reconcile. The benchmark still identifies expected corrections by original/replacement pair, so it does not establish positional precision.

### ACX-0003 — stale work blocks fresh work

With the production background lane and coordinator but a controlled in-memory judge, a fresh sentence entered the judge at 0.0075 ms p50 / 0.1938 ms p95. When an already-invalidated judgment retained the single-flight slot for 250 ms, the fresh sentence entered at 251.629 ms p50 / 264.806 ms p95. ACX-0003 proves head-of-line blocking only. Separate coordinator code and tests establish epoch rejection; this run's held verdict abstained and could not test a stale changing outcome.

Raw output: `benchmark/runs/interactive-autocorrect/ACX-0003/probe-output.json` (SHA-256 `1C94A9CAC956728FFABED3249037AAAAABF339500B0D31ECB37657FF69B054B2`). Native-model cancellation and preemption remain untested.

### ACX-0007 — changing stale verdicts are rejected but retain lane ownership

On clean HEAD `27f3fcb0`, all 20 held first verdicts selected a real nonliteral edit. The probe's injector throws if any stale edit reaches it; all 20 trials completed without reaching it. This directly exercises epoch rejection of a changing obsolete verdict in the controlled coordinator path.

The same trials reproduce head-of-line blocking: baseline judge entry was 0.0066 ms p50 / 0.0111 ms p95, while the fresh request entered at 253.379 ms p50 / 262.041 ms p95 under a 250 ms hold. Raw output SHA-256: `D2E2F76B44CEB14E1B8CFD8178BAF39FAA3407BF2E395A60DD4490DA570FB15A`.

**Established:** stale application is rejected in this controlled path. **Interpretation:** an anticipated or slow scorer must not share fast-path ownership unless native cancellation releases it promptly. **Unknown:** actual DirectML cancellation, preemption, and process-isolation behavior.

## Provisional Pareto frontier

Wave-1 audited validation: the final Debug x64 build completed with zero warnings/errors and `Deckle.Autocorrect.Tests` passed 751/751 executed tests. Nine explicit maintenance probes remain opt-in and were not counted as executed.

ACX-0006 supersedes ACX-0002 as the reproducible deterministic baseline and is provisionally non-dominated inside its limited deterministic/simulated scope. Neither positional nor applied precision is measured, so it cannot yet be compared to interactive whole-sentence candidates on product quality.

ACX-0003 does not add a quality/latency candidate to the frontier; it establishes a constraint: speculative architectures that retain obsolete single-flight work are ineligible for a direct-interaction Pareto claim until blocked-useful-work and stale compute are measured.

### ACX-0004 — held modifier crosses the authorization boundary

**Established in the controlled engine path.** A late exact-sentence verdict was released after a generic `VK_SHIFT` key-down and before key-up. Both trials produced an applied event (`2/2`). The fully instrumented second trial reported `applied=1`, `injector_calls=1`, and the simulated visible text `Il y a une seule erreur.`. Modifier-only transitions therefore do not currently invalidate the sentence verdict or prevent the injector call.

Raw outputs are under `benchmark/runs/interactive-autocorrect/ACX-0004/`; the decisive TRX is `modifier-authorization-attempt-2.trx` (SHA-256 `F5E8C1AAAEB18AD3957BCCC711BA5A5E05B47CAE89AA1F92C762A86714E13E8E`). The temporary desired-safety test was removed, its source was preserved as `probes/ACX-0004-modifier-authorization.patch`, and the original sentence scenario class then passed 22/22 tests.

**Interpretation.** The current continuously observed path can authorize a real `SendInput` burst while a modifier is active. Any future fast or anticipatory path needs a separate application lease that requires neutral modifier/composition state and invalidates on modifier transitions.

**Still unknown.** This experiment used a simulated target and injector. It does not establish whether Shift, Ctrl, Alt, AltGr, Win, IME, sticky keys, or application-specific deletion semantics cause collateral real text changes. Those belong to the explicit, allowlisted Playground phase.

### ACX-0005 — pre-terminal anticipation ceiling

**Established on the exploratory consented temporal stream.** The source stayed byte-for-byte stable while 3,528 runs were analyzed. Among 530 first terminal gestures, 498 had a known preceding literal and usable per-character timing. The last-literal-to-terminal gap was 353 ms p50, 1,695 ms p95, 3,352 ms p99, and 4,373 ms maximum.

With exact punctuation granted for free, zero candidate preparation, zero queue delay, and computation starting immediately after the preceding key, a 945 ms decision was ready before punctuation for 69/498 gestures (13.86%). A 150 ms decision reached 474/498 (95.18%); a 250 ms decision reached 364/498 (73.09%). Waiting 100 ms to trigger reduced the 150 ms budget to 73.09% and the 945 ms budget to 10.44%.

Raw aggregate output: `benchmark/runs/interactive-autocorrect/ACX-0005/probe-output.json` (SHA-256 `3435B68BC5AEA45118A7DBB1A009DBF0B35F0DAB716D9D1CB1EE9667B3661A19`). It contains timings only, never typed text. `reconciliation.json` independently recomputes the 945 ms and 150 ms counts and nearest-rank percentiles.

**Interpretation.** On this selected timing sample, a hypothetical fixed 945 ms decision has a 13.86% optimistic readiness ceiling. This does not estimate the current Qwen latency distribution or candidate-eligible readiness. The same sample suggests that a fixed 50–150 ms decider deserves a joined eligibility experiment; a pause trigger spends the available gap quickly.

**Still unknown.** These gestures are not filtered to transactions where Deckle generated a useful candidate. The subset of actual correction opportunities may have a different timing distribution. The next anticipation study must join exact transaction eligibility to the same timing stream before a product coverage claim.

### ACX-0008 — exact-prefix timing reproduction

ACX-0008 replays exactly the first 1,033,605 bytes under clean HEAD `27f3fcb0`. The prefix hash is `4F0379B509717C4A3B356E152801180A033EB7B4E4AC55803F8B4151EB6631FB`, ends at a line boundary, remained stable across both reads, and reproduces every ACX-0005 parse count, percentile, and readiness count. The raw timing output SHA-256 is `A988050FB4D01994DC02FCE41C3B666904156C69A99345F6121D95785A18A7D4`. It contains no typed text, but retains the personal 498-element gap vector locally and is therefore private rather than aggregate-only.

This promotes the fixed-duration selected-sample oracle from conditional to reproducible. It does not broaden the claim: 69/498 is still an optimistic ceiling for a hypothetical fixed 945 ms duration, not candidate-eligible Qwen coverage.

### ACX-0009 — Qwen semantics established, stage profile invalidated

ACX-0009 ran the isolated Qwen3-1.7B DirectML profiler from clean HEAD
`27e9c248`. The post-commit build completed with zero warnings and errors. The
single fresh process completed in 337.164 seconds with no stderr or scoring
error. Profiled and ordinary outcomes were exactly equal across all five
overhead pairs, all four 2/4/8/13-candidate controls, and all 35 visible quality
cases. This establishes that the collector preserved the scorer's semantic
result in this run.

The predefined observer gate failed. At two candidates, the profiled marginal
median was 903.579 ms and the ordinary marginal median 794.012 ms: +109.567 ms
and +13.799%, above both the 2 ms and 3% limits. This is not established observer
cost. Every first call in a pair took about 902–932 ms and every immediately
repeated second call 747–794 ms, regardless of method. Five alternating pairs
left a 3/2 call-position imbalance that converted this roughly 150 ms repeat
effect into an apparent marginal overhead. Therefore no generator, forward,
readback, log-softmax, disposal, or reverse-order stage fraction is
decision-valid, and no measured delta is subtracted from the totals.

The instrumented hot measurements remain descriptive leads only:

| Candidates | n | Profiled p50 | Profiled p95 | Maximum |
|---:|---:|---:|---:|---:|
| 2 | 20 | 905.508 ms | 943.737 ms | 951.015 ms |
| 4 | 20 | 1,450.569 ms | 1,703.818 ms | 1,726.928 ms |
| 8 | 20 | 3,266.370 ms | 3,581.509 ms | 3,617.623 ms |
| 13 | 20 | 6,647.701 ms | 6,987.506 ms | 7,035.860 ms |

Latency fit candidate count with R² 0.977964 and the submitted-token proxy with
R² 0.995569. Because the profiler overhead is invalid and the two predictors are
coupled in one fixture family, these are exploratory scaling signals rather than
causal evidence for batching.

On the visible development corpus at raw margin zero, the combined two-order
decision was correct on 33/35 cases, versus 32/35 forward-only and 27/35
reverse-only, with 9 order disagreements. It selected 15 correct nonliteral
edits out of 16 applications. The two errors were `ou_question` (regrettable
keep) and `literal_ratures` (false correction, margin 1.480). A 1.0 threshold
still retained that false correction while reducing correct applied edits to
10. These 35 visible cases establish neither field precision nor the 99.9%
ambition; they do show that threshold monotonicity alone does not rank every
harmful case below useful ones.

Raw output SHA-256:
`7514E731802F619FE2302515E86DD467B57AB2FEB2B174EF731EEC10FC1DFBC6`.
Reconciliation SHA-256:
`EE78358532EB251F55513BD2626AD14406EC8D85B0670414753F0F833188FED0`.
The next discriminator starts with matched four-call crossover blocks,
alternating `P-O-O-P` and `O-P-P-O` independently from stratum order and candidate
rotation. Profile attribution must pass both practical limits with a predeclared
uncertainty upper bound, not point-estimate cancellation. Ordinary latency then
gets a separate schedule without immediate duplicate calls; profiled stage
distributions run separately only if calibration passes. API-boundary wall time
is the strongest permitted stage claim without an external GPU or ORT trace.

## Refuted or dominated families

No complete end-to-end Pareto candidate is dominated yet. Two narrower architecture claims are refuted within their stated scopes: terminal Qwen3-1.7B DirectML as-is is not a direct-interaction path at the measured warm duration, and slow speculative work cannot share the current single-flight lane without blocking fresh useful work. Qwen as teacher/shadow/cache target and speculation with separate ownership remain active possibilities.

## Active uncertainties

- Whether a valid stage profile exposes enough removable Qwen work to justify a
  shared-prefix or batching prototype.
- Whether speculative work creates usable lead time under real typing cadence.
- Whether a compact discriminator can meet the precision posture at useful coverage.
- How much end-to-end latency is inference versus target verification and edit observation.
- Which Windows surfaces can provide exact, low-latency anchoring without TSF.
