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

## Refuted or dominated families

No complete end-to-end Pareto candidate is dominated yet. Two narrower architecture claims are refuted within their stated scopes: terminal Qwen3-1.7B DirectML as-is is not a direct-interaction path at the measured warm duration, and slow speculative work cannot share the current single-flight lane without blocking fresh useful work. Qwen as teacher/shadow/cache target and speculation with separate ownership remain active possibilities.

## Active uncertainties

- Whether terminal Qwen latency is reducible enough to matter directly.
- Whether speculative work creates usable lead time under real typing cadence.
- Whether a compact discriminator can meet the precision posture at useful coverage.
- How much end-to-end latency is inference versus target verification and edit observation.
- Which Windows surfaces can provide exact, low-latency anchoring without TSF.
