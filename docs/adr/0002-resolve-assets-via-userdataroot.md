---
name: adr-0002-resolve-assets-via-userdataroot
description: "Records that Deckle resolves native runtime assets and speech models from UserDataRoot (%LOCALAPPDATA%\\Deckle\\) exclusively, replacing a multi-fallback cascade. Read before touching asset/model path resolution or adding a fallback path."
type: adr
---

# ADR-0002 — Resolve assets via UserDataRoot exclusively

**Status** — accepted 2026-05-23 (retroactive record of the 2026-04-30 decision)

## Context

Deckle depends on two families of runtime assets not embedded in the binary: the native whisper.cpp + ggml-vulkan DLLs (~50 MB), and the Whisper models (small ~500 MB, large-v3 ~3 GB). Before 2026-04-30 the app resolved them through a fallback cascade — `AppContext.BaseDirectory`, then a walk-up to `<repo>/native` or `<repo>/models` in dev mode, then junctions for worktrees, then `UserDataRoot` as a last resort. The cascade made provisioning diagnosis unstable: a dev build could land on the repo copy, a publish build on `%LOCALAPPDATA%`, a worktree on a junction — each case needing its own setup.

## Options considered

- **A. Keep the multi-fallback cascade.** Flexible in dev (several usable paths), maximalist in provisioning. A continuous reasoning and diagnosis cost.
- **B. Force `<repo>/native` and `<repo>/models` even in publish.** An "assets in the repo" model, simple for a cloner. Incompatible with the large models (a 3 GB versioned file is rejected) and complicates cohabitation between worktrees that would each carry their own copy.
- **C. Force `UserDataRoot` exclusively.** One source of truth — `%LOCALAPPDATA%\Deckle\native\` and `%LOCALAPPDATA%\Deckle\models\` by default. Any binary (dev build, worktree, publish) always reads there. The repo contains neither `native/` nor `models/`. The first-run wizard or `scripts/lib/setup-assets.ps1` populates it.

## Decision

Option C. `UserDataRoot` (default `%LOCALAPPDATA%\Deckle\`) is the **only** resolution path for native assets and models. `NativeRuntime.IsInstalled()` checks only `NativeDirectory` under `UserDataRoot`. Model resolution is inlined to `Path.Combine(UserDataRoot, "models")`. The app csproj no longer copies native DLLs next to the exe. `scripts/lib/build-run.ps1` no longer synchronizes worktree junctions — worktrees share the assets via `UserDataRoot` because no alternative path remains to synchronize. Script switches: `-DataRoot` (override the target), `-AlsoInRepo` (dev mode, also fill `<repo>/native` and `<repo>/models`, only to produce a `native-vX.Y.Z` release), `-WithLarge` (fetch `ggml-large-v3.bin`), `-Force` (re-download).

## Consequences

Easier: one path to check when diagnosing a missing asset; trivial cohabitation of dev and publish builds (they read the same `UserDataRoot`); worktrees need no junction or copy; a future MSIX packaging stays open with no asset-resolution debt.

Harder: a fresh clone MUST run the first-run wizard or `setup-assets.ps1` before the first runtime build — the local `<repo>/native` copy is no longer an emergency landing.

Impossible: copying the DLLs next to the exe to ship a portable standalone release. Acceptable because Deckle distribution stays source-only and the first-run wizard handles runtime installation on the target machine.
