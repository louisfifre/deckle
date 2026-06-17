---
name: handoff-benchmark-reorg
description: "Session handoff for the benchmark workspace reorganization."
type: handoff
module: benchmark
---

# Benchmark Handoff — 2026-06-17

## Intent

Reorganize `benchmark/` so reusable benchmark infrastructure is separated from
ASR-specific material, while keeping the ASR research work available for reuse
or future extraction.

## State

The benchmark tree has been reorganized around four responsibilities:

- `benchmark/lib/` — cross-benchmark infrastructure: paths, env loading,
  event logs, resource monitor, base compatibility.
- `benchmark/viewers/` — generic benchmark result viewers.
- `benchmark/asr/` — ASR-specific code: corpus loader, transcription sources,
  ASR judges, ASR metrics, prompts, frozen speech studies.
- `benchmark/autoresearch/` — generic autoresearch campaign structure:
  campaigns, prompts, metrics, judges, runners.

Specific cleanups done:

- Removed tracked `benchmark/.env.example`; local secrets now live only in the
  ignored `benchmark/.env`.
- Removed `pregenerate_groundtruth_gemini.py`.
- Renamed and generalized `build_corpus_voxtral_val_30.py` to
  `benchmark/asr/build_corpus.py` with `--corpus`.
- Kept ASR-specific Python code under `benchmark/asr/lib/`, imported as
  `asr.lib.*` so it does not collide with root `benchmark/lib`.
- Moved benchmark MSBuild opt-out files back to `benchmark/`.
- Deleted the stray untracked `WindowsAppSDK/` clone and cleaned generated
  `bin/`, `obj/`, and `__pycache__` output.

Validation already run:

- `python -m compileall -q .\benchmark`
- `python -c "import sys; sys.path.insert(0, r'D:\projects\ai\deckle\benchmark'); import lib.paths; import asr.lib.corpus; print('ok')"`
- `dotnet build .\benchmark\asr\studies\PhiBench\PhiBench.csproj -c Debug -p:Platform=x64 /nr:false /p:UseSharedCompilation=false`
- `git diff --cached --check`
- Search confirmed the old names are gone:
  `asr_lib`, `benchmark/asr/.env`, `.env.example`,
  `build_corpus_voxtral_val_30`, `pregenerate_groundtruth_gemini`.

## In Flight

Louis reports that the menu no longer works. That was not diagnosed in this
session. Do not assume it is fixed by the benchmark reorganization.

Likely next step: start from `scripts/README.md`, `deckle.ps1`, and
`scripts/lib/_menu.psm1`, then run the menu entry point and inspect the failure.

## Resume Here

Project root: `D:\projects\ai\deckle`

Branch: `main`

Open by reading:

- `benchmark/README.md`
- `benchmark/CLAUDE.md`
- `benchmark/asr/README.md`
- `benchmark/asr/CLAUDE.md`
- `scripts/README.md`

Then investigate the menu failure as a separate workstream.
