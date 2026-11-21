---
name: Bench
description: Takes a dated measurement pass of Deckle on a separate instance and compares two passes, to give a performance fix its before and after.
order: 210
---

# Bench

Two scripts: `perf-bench.ps1` takes a pass, `perf-compare.ps1` compares two. A pass is one JSON file of named metrics in `artifacts/bench/`, named `perf-<date>_<time>[-<label>]-<configuration>.json`.

The pass runs `scripts/self-check.ps1` underneath: the built `Deckle.exe` of the checkout, started with its own data root, beside the maintainer's Deckle, which it never touches. It builds nothing.

## What a pass measures

| Metric | Where it comes from |
|---|---|
| `startup.ready_ms` | process start to the end of the self-check's wiring (settings surface up). Not the resident boot, which the self-check leaves out: compare it with itself only |
| `window.<name>.create_ms` | the `WindowLoadComplete` event, a window's first-open cost, read from the run's `log.jsonl` |
| `page.<window>/<page>.load_ms` | the load time the self-check's walk already measures for each Settings and Mail page |
| `memory.*`, `process.*` | working set, private working set, private bytes, handles and threads, taken once the walk has opened every window |
| `dictation.*` | one take through the real engine, on a test audio: wall time, real-time factor, and the engine's own `PipelineTimings` phases (model load, ASR init, ASR) |
| `memory_after_dictation.*` | the footprint after the take and the engine's release, a leak check |

The test audio is the speech clip the repository already ships (`src/Deckle.App/Assets/Sounds/speech.wav`, synthetic, no personal data) repeated six times, about fifteen seconds. `-AudioFile` swaps it; never give it a recording of a person.

The dictation take needs the native runtime and a speech model. The run links the maintainer's `native\` and `models\` folders into its fresh root (the self-check's `-LinkRuntime`, read only in practice) and the engine is composed as the resident boot composes it, so the take uses the speech engine chosen in Settings, Whisper by default, and the largest installed model. On a machine without them the take is reported skipped in the pass's `notes`, and the rest of the pass is still good. `-SkipDictation` leaves it out, and then nothing of the maintainer's is linked.

## Taking a before and after for a fix

1. Build the code before the fix: `pwsh -NoProfile -File scripts/build-locked.ps1 -Project src/Deckle.App/Deckle.App.csproj`.
2. Take two passes: `pwsh -NoProfile -File scripts/bench/perf-bench.ps1 -Label before`, twice. The second tells how much the machine moves on its own.
3. Apply the fix, build again, take two passes with `-Label after`.
4. Compare: `pwsh -NoProfile -File scripts/bench/perf-compare.ps1 -Before <file or latest-3> -After <file or latest>`. A file name given alone is looked up in `artifacts/bench/`; `latest` is the newest pass and `latest-N` the one N before it.
5. Quote the lines of the metric the fix targets, with both values, in the commit or the report.

A metric flagged in both comparisons (the first pass against the third, the second against the fourth) is a real change. One flagged once is probably noise: a single page load moves by a hundred milliseconds from one run to the next, and another session's build or self-check running at the same time moves everything. Take the pass when the machine is quiet.

`perf-compare.ps1` flags a regression when a metric got worse by more than `-ThresholdPercent` (25 by default) and by more than its noise floor (50 ms, 10 MB, 25 handles, 0.2 of real time), and exits 1 then, 2 when the passes cannot be compared (a different configuration or target). Passes taken on Debug and Release are never compared with each other.

`-Only <target>` takes a pass of one window (`-Only settings`), faster; the dictation take is left out, the memory figures cover the windows that target opened, and such a pass compares only with another of the same target.
