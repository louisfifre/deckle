---
name: readme-scripts
description: "Dev workflows entry point for Deckle: the deckle.ps1 menu, the worker scripts under lib/, the TREE.md pre-commit hook, generated-docs automation, and the three native-runtime sourcing modes. Read before running, modifying, or extending a script under scripts/."
type: module-readme
module: scripts
---

# `scripts/` — Deckle dev workflows

All scripts target PowerShell 7+. The single entry point lives at
[`deckle.ps1`](deckle.ps1); the worker scripts it dispatches to live
under [`lib/`](lib/) and stay usable on their own CLI for automation.

## Entry point — `deckle.ps1`

`deckle.ps1` is what F5 runs in VSCodium (see
[`.vscode/launch.json`](../.vscode/launch.json)) and what you call from
a terminal for daily work. It opens an arrow-key menu grouping every
dev action by purpose. The launcher uses a full-screen terminal flow:
while you navigate, it runs in the terminal's alternate screen buffer, so
`More`, worktree selection, and version selection replace the current screen
instead of appending below. When a concrete action starts, the normal terminal
is restored first so build and publish logs remain visible. The release,
maintenance, and setup submenus keep the same grid style:
their first selectable cell is always `< Back`, so a mistaken submenu entry is
one Enter away from returning.

| Section | Action | Per-worktree? | Delegates to |
|---|---|:---:|---|
| **Run** | Launch app (Release / Debug) | yes | `lib/launch-app.ps1 -Configuration Release\|Debug` |
|  | Build and run app (Release / Debug) | yes | `lib/build-run.ps1 -Configuration Release\|Debug` |
|  | Build app without running (Release / Debug) | yes | `lib/build-run.ps1 -Configuration Release\|Debug -NoRun` |
| **Project** | Update version | yes | `lib/cut-version.ps1` |
|  | Install / update Anytype MCP | no | `lib/install-anytype-mcp.ps1` (confirms first) |
| **More > Release** | Publish app release | yes | `lib/publish-app.ps1 -Publish` (confirms first) |
|  | Prepare app release artifacts | yes | `lib/publish-app.ps1` |
|  | Prepare native runtime release | no | `lib/publish-native-runtime.ps1` (publishing confirms first) |
| **More > Maintenance** | Clean build outputs | yes | `lib/clean.ps1` |
|  | Stop .NET build servers | no | `lib/stop-build-servers.ps1` |
|  | Show module stats | yes | `lib/stats.ps1` |
|  | Update README pulse | yes | `lib/update-readme-stats.ps1` |
|  | Update changelog | yes | `lib/changelog.ps1` |
| **More > Setup** | Bootstrap dev environment | no | `lib/bootstrap-dev-env.ps1` |
|  | Set up runtime assets | no | `lib/setup-assets.ps1` |
|  | Install git hooks | no | `lib/install-hooks.ps1` |

Per-worktree actions prompt for a worktree right after the action is
picked (auto-resolved when only the main repo exists). Global actions
go straight to a short parameter prompt where needed. Once a concrete action
has been launched, the menu exits instead of returning to the launcher. Use
`Quit` or Ctrl+C to leave without running an action.

## Worker scripts — `lib/`

Each worker is callable directly from a terminal or a `launch.json`
profile — `deckle.ps1` is purely additive.

`lib/` keeps the public script entry points at the top level so existing
direct calls do not break. Internal implementation is split by role:
`lib/menu/` owns the reusable terminal UI engine, while `lib/launcher/`
owns the `deckle.ps1` menu context, actions, and submenu definitions.
Concrete worker scripts end with the same human-readable action summary:
a short sentence, a `Workflow`, a `Result` (`Success`, `Failed`, `Partial`,
or `Skipped`), then the fields that matter for that workflow. The detailed
step logs stay above it for diagnosis.

| File | Purpose | Common switches |
|---|---|---|
| [`lib/launch-app.ps1`](lib/launch-app.ps1) | Kill running `Deckle.exe` and launch the freshest already-built app executable. Does not build. | `-Configuration Debug\|Release`, `-Target <worktree>`, `-Pick`, `-Wait` |
| [`lib/build-run.ps1`](lib/build-run.ps1) | Kill running `Deckle.exe`, build via `dotnet build` without persistent MSBuild/Roslyn build servers, and launch the freshly built exe through ShellExecute. | `-Configuration Debug\|Release`, `-NoRun`, `-Wait`, `-Target <worktree>`, `-Pick`, `-NoAutoRestart` |
| [`lib/clean.ps1`](lib/clean.ps1) | Kill running `Deckle.exe` (it locks the output), stop .NET build servers left by manual/agent builds, then remove the consolidated `artifacts/{bin,obj,publish,package}/` plus any straggler `bin/`+`obj/` under `src/`, `tests/`, and benchmark study folders. Keeps `artifacts/Deckle-v*` release staging unless `-IncludeReleases`. Guards against symlinks / junctions. Reports total freed bytes. | `-Target <worktree>`, `-Pick`, `-IncludeReleases` |
| [`lib/stop-build-servers.ps1`](lib/stop-build-servers.ps1) | Stop machine-wide .NET build servers left by local, menu, or agent builds. Useful when Task Manager shows lingering `.NET Host` / `VBCSCompiler` rows but you do not want to delete build outputs. | |
| [`lib/stats.ps1`](lib/stats.ps1) | Walk every `.csproj` under `src/`, build a per-file inventory, highlight files over 500 / 1000 raw lines, summarize modules by source LOC, list file types dynamically, and print the per-file module table. Excludes `bin/obj/artifacts/.vs/Properties` and generated files (`*.g.cs`, `*.g.i.cs`, `*.xaml.g.cs`). | `-Target <worktree>`, `-Pick`, `-Json <path>` |
| [`lib/setup-assets.ps1`](lib/setup-assets.ps1) | Populate `<UserDataRoot>\native\` and `<UserDataRoot>\models\` with the whisper.cpp DLLs, MinGW C++ runtime, and Whisper / Silero VAD models. Idempotent. See *Native runtime* below for the three sourcing modes. | `-DataRoot <path>`, `-FromRelease X.Y.Z`, `-WhisperRepo <path>`, `-WithLarge`, `-Force` |
| [`lib/bootstrap-dev-env.ps1`](lib/bootstrap-dev-env.ps1) | Provision a fresh Windows 11 machine: winget (VS 2026, .NET 10, git, gh), optional scoop Tier 2 (MinGW, CMake, Ninja, Vulkan SDK, Ollama). Probes existing state, builds a plan, asks for confirmation, then executes. Runtime assets are left to the app's first-run wizard unless explicitly requested. | `-DryRun`, `-Full`, `-Yes`, `-IncludeAssets`, `-AssetsRelease X.Y.Z` |
| [`lib/install-anytype-mcp.ps1`](lib/install-anytype-mcp.ps1) | **Maintainer-only.** Publish the Anytype MCP host into a fresh `<UserDataRoot>\mcp\anytype\versions\<timestamp>\` (outside `artifacts/`) and point the `current` junction (Scoop model) at it; `.claude.json` targets `current\` once. AI clients stop spawning — and locking — the build-output exe (the `MSB3026`-on-rebuild cause). Re-run any time to cut a new version: it republishes + re-points the junction without overwriting anything running, then prunes old versions (skipping any a live client still holds). Config repoint is a surgical text-replace of the one `command` ending in `Deckle.Anytype.Mcp.exe`, backed up once, idempotent after. | `-Target <worktree>`, `-Pick`, `-DataRoot <path>`, `-NoConfig`, `-ConfigOnly` |
| [`lib/cut-version.ps1`](lib/cut-version.ps1) | Bump the single source-of-truth `<Version>` in `Deckle.App.csproj`, commit only that one-line change as `chore(release): vX.Y.Z`, and create the local tag. It does not push. Run before release artifact preparation / publishing so the built app carries the intended version. | `-Target <worktree>`, `-Pick`, `-Bump patch\|minor\|major`, `-NoCommit` |
| [`lib/install-hooks.ps1`](lib/install-hooks.ps1) | Install the local git hooks sourced from `scripts/hooks/` into `.git/hooks/` and register the local `merge.ours` driver used by `TREE.md`. | |
| [`lib/update-readme-stats.ps1`](lib/update-readme-stats.ps1) | Regenerate the README `Development pulse` section from local Git history. Also used by the monthly GitHub Action. | `-Target <worktree>`, `-Pick`, `-ReadmePath <path>` |
| [`lib/changelog.ps1`](lib/changelog.ps1) | Generate `CHANGELOG.md` and release notes from the Conventional-Commit history — plain `git log` + PowerShell, no external tool or API. Default regenerates the whole `CHANGELOG.md` from the `v0.4.0` floor forward; `-NotesFor X.Y.Z` emits a single version's section for `gh … --notes-file` (consumed by `publish-app.ps1`). | `-Target <worktree>`, `-Pick`, `-NotesFor X.Y.Z`, `-OutFile <path>` |
| [`lib/publish-native-runtime.ps1`](lib/publish-native-runtime.ps1) | **Maintainer-only.** Assemble the native runtime zip (8 DLLs + `PROVENANCE.txt` + `SHA256SUMS`) from a local whisper.cpp build tree, optionally publish it to GitHub Release as `native-vX.Y.Z`. | `-Version X.Y.Z`, `-WhisperRepo <path>`, `-OutDir <path>`, `-Publish`, `-Notes <path>` |
| [`lib/action-summary.ps1`](lib/action-summary.ps1) | Internal shared writer for the final action summary used by worker scripts. | |
| [`lib/_menu.psm1`](lib/_menu.psm1) | Public facade over `lib/menu/`, exposing `Start-MenuSession`, `Stop-MenuSession`, `Suspend-MenuSession`, `Select-Worktree`, `Select-Action`, `Select-YesNo`, and `Select-Grid`. Pickers can clear the terminal before rendering so nested screens replace each other; the launcher can also use the terminal alternate screen buffer for a full-screen flow. Imported by `deckle.ps1`, `launch-app.ps1 -Pick`, `build-run.ps1 -Pick`, `clean.ps1 -Pick`, `stats.ps1 -Pick`, `update-readme-stats.ps1 -Pick`, `changelog.ps1 -Pick`. **Not an entry point.** |

## Git hooks — TREE.md auto-update

A `pre-commit` hook regenerates [`TREE.md`](../TREE.md) at the repo root before every commit and stages it automatically, so the repo always carries an up-to-date view of its tracked tree. Source in [`hooks/pre-commit`](hooks/pre-commit), local install via [`lib/install-hooks.ps1`](lib/install-hooks.ps1) or the `deckle.ps1` Setup menu to run once after a clone — hooks live under `.git/hooks/` and are not versioned by git.

The hook delegates to [`hooks/update-tree.ps1`](hooks/update-tree.ps1), which rebuilds `TREE.md` from `git ls-files` (flat view, zero gitignored file, no annotation). It can also run by hand to refresh outside a commit: `pwsh scripts/hooks/update-tree.ps1`.

## Generated docs automation

The root README carries a small generated `Development pulse` section bounded by invisible HTML comments. Regenerate it locally through the menu (`Update README pulse`) or directly:

```powershell
pwsh scripts/lib/update-readme-stats.ps1
```

GitHub also runs `.github/workflows/update-readme-stats.yml` monthly and on manual dispatch. The workflow checks out full history (`fetch-depth: 0`), runs the same script, and commits `README.md` only when the generated section changed.

`CHANGELOG.md` is the same maintenance family: it is regenerated from local Git history, not hand-edited and not a publish action. Regenerate it locally through the menu (`Update changelog`) or directly:

```powershell
pwsh scripts/lib/changelog.ps1
```

## Native runtime — three sourcing modes

The app's first launch opens the in-app setup wizard when native DLLs or
models are missing. The F5 menu also exposes `Set up runtime assets` as a
developer shortcut over `lib/setup-assets.ps1`, which provisions the 8
native DLLs (5 whisper.cpp
Vulkan + 3 MinGW C++ runtime) through one of three paths:

1. **`-FromRelease <X.Y.Z>` (default for non-rebuilders).** Fetches
   `deckle-native-<X.Y.Z>.zip` from the Deckle GitHub Release and
   extracts the catalog DLLs in place. No local whisper.cpp clone
   needed. Same source as the first-run wizard's auto-download path.

2. **`-WhisperRepo <path>` (for whisper.cpp rebuilders).** Copies DLLs
   from a local whisper.cpp build tree (`<path>\build\bin\` plus the
   MinGW runtime from Scoop). Use when iterating on whisper.cpp source
   — recompile, point the script at your tree, the bundle on
   `<UserDataRoot>` refreshes without going through GitHub. Falls back
   to `$env:DECKLE_WHISPER_REPO` and then to a sibling
   `<repo>\..\whisper.cpp` clone.

3. **Skip.** When neither path resolves to a valid build tree, the
   native step is skipped with a warning. Useful when only models need
   refreshing on a machine without a build tree.

The Whisper models are pulled from HuggingFace; the Silero VAD model is
pulled from GitHub (snakers4/silero-vad, pinned to the v6.2 tag).
Both happen regardless of native runtime sourcing mode.

## Post-build HUD topmost mitigation

`lib/build-run.ps1` passes `--post-build` to the launched
`Deckle.exe` by default. The app finishes its boot, waits ~800ms,
then re-launches itself once via `cmd /c start`, then exits. The
second instance inherits a clean foreground state and the HUD's
`WS_EX_TOPMOST` flag applies correctly on the first recording.
Disable with `-NoAutoRestart` if you need a stable PID (attached
debugger, log capture). See `App.RestartViaShellExecute()` and the
`--post-build` parsing in `App.OnLaunched`.

This is a workaround for the visible symptom only — the underlying
topmost-loss behaviour (HUD loses topmost when another window grabs
foreground, especially other WinUI 3 apps) is a separate investigation
gated on the EventSource logging refactor.

## What is *not* here

- **MSIX / packaged installer.** No MSIX or Store package — Deckle ships
  *unpackaged*. The GitHub Release (a pre-release while on 0.x), cut by
  `lib/publish-app.ps1 -Publish`, carries two assets: the headline
  `Deckle-Setup-vX.Y.Z-win-x64.exe` — the installer stub the end user downloads
  and runs — and the self-contained app payload it fetches (`Deckle-vX.Y.Z.zip`
  + `.sha256`). The `Deckle.Installer` stub resolves the latest release via the
  GitHub API, downloads the payload, sha256-verifies it, and installs per-user
  (GitHub auto-attaches the source-code archives too). Building from source via
  `lib/build-run.ps1` (or the launcher) stays the dev path.
- **CI / GitHub Actions.** None for now — personal project. Both publish
  flows — the app ZIP and the native runtime — are cut manually by the
  maintainer.
- **Source mirror of whisper.cpp.** The repo no longer carries a
  `whisper.cpp/` clone. Rebuilders clone it themselves alongside the
  Deckle repo, build it locally, and point `-WhisperRepo` at it.
