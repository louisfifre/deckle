---
name: readme-scripts
description: "Dev workflows entry point for Deckle: the deckle.ps1 menu, the worker scripts under lib/, the TREE.md pre-commit hook, generated-docs automation, and the three native-runtime sourcing modes. Read before running, modifying, or extending a script under scripts/."
type: module-readme
module: scripts
---

# `scripts/` — Deckle dev workflows

All scripts target PowerShell 7+. The single entry point lives at [`deckle.ps1`](deckle.ps1); the worker scripts it dispatches to live under [`lib/`](lib/) and stay usable on their own CLI for automation.

## Entry point — `deckle.ps1`

`deckle.ps1` is what F5 runs in VSCodium (see [`.vscode/launch.json`](../.vscode/launch.json)) and what you call from a terminal for daily work. It opens an arrow-key menu grouping every dev action by purpose. Every surface uses the same compact two-line Deckle banner. Workspace ends with a narrow trailing `Quit` action reached from its final row, while categories inside each submenu stay on adjacent rows. The launcher runs in the terminal's alternate screen buffer, so nested flows replace the current screen instead of appending below. Concrete actions keep their command grid visible, stream sanitized output into the remaining viewport, then return control to the same menu with the result preserved.

| Section | Action | Per-worktree? | Delegates to |
|---|---|:---:|---|
| **Run** | Launch app (Release / Debug) | yes | `lib/launch-app.ps1 -Configuration Release\|Debug` |
|  | Build and run app (Release / Debug) | yes | `lib/build-run.ps1 -Configuration Release\|Debug` |
|  | Build app without running (Release / Debug) | yes | `lib/build-run.ps1 -Configuration Release\|Debug -NoRun` |
| **Workspace > Project** | Update README pulse | yes | `lib/update-readme-stats.ps1 -Commit` |
|  | Update changelog | yes | `lib/changelog.ps1` |
|  | Record version | yes | `lib/record-version.ps1 -Push` |
| **Workspace > Release > Publish** | App release | yes | records a pending version when needed, then `lib/publish-app.ps1 -Publish` (confirms first) |
|  | Native runtime | yes | derives the next `native-vX.Y.Z` from whisper.cpp and published bundles, packages an existing `build/bin`, and publishes it; it runs no build (confirms first) |
| **Workspace > Release > Prepare** | App artifacts | yes | `lib/publish-app.ps1` |
|  | Native runtime | yes | derives the next `native-vX.Y.Z` and packages an existing whisper.cpp `build/bin` locally; it runs no build |
| **Workspace > Maintenance** | Repository statistics | yes | targeted specifications over `lib/repository-inventory.psm1`, rendered in the maintenance viewport |
|  | Context statistics | yes | targeted specifications over `lib/context-inventory.psm1`, rendered in the maintenance viewport |
|  | Clean build outputs | yes | `lib/clean.ps1` |
|  | Stop .NET build servers | no | `lib/stop-build-servers.ps1` |
| **Workspace > Setup** | Bootstrap dev environment | no | `lib/bootstrap-dev-env.ps1` |
|  | Set up runtime assets | no | `lib/setup-assets.ps1` |
|  | Install git hooks | no | `lib/install-hooks.ps1` |

Most per-worktree actions prompt for a worktree right after the action is picked (auto-resolved when only the main repo exists). Maintenance statistics deliberately choose a scan goal first, configure it when `Custom scan…` is selected, choose the worktree second, then show a read-only review before `Run scan` becomes available. The worktree picker follows the same nested-screen grammar as the submenus: compact chrome, Back in the first action cell, a restrained section title, then one branch per row in that same fixed column while the second column remains reserved and non-navigable. Version choices use the same grid instead of opening a legacy vertical prompt. Global actions go straight to a short parameter prompt where needed. Returning from an action preserves the selected command and preferred column. Action logs reopen on their latest page, while guidance and Maintenance reports reopen at the beginning. The mouse wheel, Page Up, and Page Down move by non-overlapping result pages, and the result heading shows the current page. The launcher asks for a resize instead of wrapping when the terminal is below its supported 45-column width or the height required by the current command surface. Use `Quit` or Ctrl+C to leave without running an action.

### Targeted maintenance statistics

Repository statistics offer `Overview`, `Files to review`, `Source metrics`, and `Custom scan…`. Context statistics offer `Footprint`, `Recent changes`, and `Custom scan…`. Presets and Custom both resolve to the same scan specification: scope, included files or documents, measurements, grouping, thresholds, and optional Git activity period. Custom exposes each field as a nested menu row and accepts one relative file or folder path when the standard `src/`, `tests/`, `scripts/`, and `docs/` scopes are not enough. `Edit scan…` copies any preset into that same editor.

Every scan is read-only and starts only after the review screen. Git defines the boundary: ignored workspace data such as `artifacts/`, `.tmp/`, and `.vs/` is absent; `.git/`, absolute paths, parent traversal, `AppData.lnk`, and scopes rooted at a link or junction are rejected. Tracked links are counted without traversal, binaries contribute metadata but are never opened as text, and content metrics are skipped for files over 5 MB. A scan never deletes files and never hands a result to Cleanup automatically.

The first cycle intentionally does not claim semantic context drift, historical size growth, or comment counts. `Recent changes` reports Git modification activity, not whether prose is still true. `Footprint` reports the aggregate inventory and largest documents, not the exact instruction cost of every request: automatic instructions remain path-scoped.

## Worker scripts — `lib/`

Each worker is callable directly from a terminal or a `launch.json` profile — `deckle.ps1` is purely additive.

`lib/` keeps the public script entry points at the top level so existing direct calls do not break. Internal implementation is split by role: `lib/menu/` owns the reusable terminal UI engine, while `lib/launcher/` owns the `deckle.ps1` menu context, actions, and submenu definitions. Concrete worker scripts end with the same human-readable action summary: a short sentence, a `Workflow`, a `Result` (`Success`, `Failed`, `Partial`, or `Skipped`), then the fields that matter for that workflow. The detailed step logs stay above it for diagnosis.

| File | Purpose | Common switches |
|---|---|---|
| [`lib/launch-app.ps1`](lib/launch-app.ps1) | Kill running `Deckle.exe` and launch the freshest already-built app executable. Does not build. | `-Configuration Debug\|Release`, `-Target <worktree>`, `-Pick`, `-Wait` |
| [`lib/build-run.ps1`](lib/build-run.ps1) | Kill running `Deckle.exe`, build via `dotnet build` without persistent MSBuild/Roslyn build servers, and launch the freshly built exe through ShellExecute. | `-Configuration Debug\|Release`, `-NoRun`, `-Wait`, `-Target <worktree>`, `-Pick`, `-NoAutoRestart` |
| [`lib/clean.ps1`](lib/clean.ps1) | Kill running `Deckle.exe` (it locks the output), stop .NET build servers left by manual/agent builds, then remove the consolidated `artifacts/{bin,obj,publish,package}/` plus any straggler `bin/`+`obj/` under `src/`, `tests/`, and benchmark study folders. Keeps `artifacts/Deckle-v*` release staging unless `-IncludeReleases`. Guards against symlinks / junctions. Reports total freed bytes. | `-Target <worktree>`, `-Pick`, `-IncludeReleases` |
| [`lib/stop-build-servers.ps1`](lib/stop-build-servers.ps1) | Stop machine-wide .NET build servers left by local, menu, or agent builds. Useful when Task Manager shows lingering `.NET Host` / `VBCSCompiler` rows but you do not want to delete build outputs. | |
| [`lib/stats.ps1`](lib/stats.ps1) | Walk every `.csproj` under `src/`, build a per-file inventory, highlight C# files over 400 / 600 effective LOC and other text files over 500 / 1000 raw lines, summarize modules by source LOC, list tracked repository file types dynamically, and print the per-file module table. Git defines the repository boundary, so ignored workspace data is excluded and tracked links are counted without traversal. Generated module files are also excluded. | `-Target <worktree>`, `-Pick`, `-Json <path>`; `-PassThru` for structured output |
| [`lib/repository-inventory.psm1`](lib/repository-inventory.psm1) | Reusable tracked-file inventory for targeted scans. Filters path and file set before content reads, works without `src/` or `.csproj`, groups by extension or top-level folder, emits common threshold findings, and counts tracked links without traversal. | internal module; `Invoke-RepositoryInventory` |
| [`lib/inspect-context.ps1`](lib/inspect-context.ps1) | Inventory tracked Markdown documents by loading mode, Git modification date, additions over 1/7/30 days, heading count, exact size and line count, plus a model-independent token estimate. Separates automatically scoped instructions from references that must be opened explicitly. | `-Target <worktree>`, `-Pick`, `-RelativePath <path>`, `-LoadingMode <name[]>`, `-DocumentType <name[]>`, `-SkipActivity`, `-Json <path>`; `-PassThru` includes documents |
| [`lib/validate-resources.ps1`](lib/validate-resources.ps1) | Audit every module `Resources.resw` against static `x:Uid`, `Loc.Get*`, descriptor literals, and the explicit dynamic-key allowlist. Reports missing keys, required-mirror divergences, and potentially unused copies without modifying resources. | `-Target <worktree>`, `-Pick`, `-Allowlist <path>`, `-Json <path>`, `-FailOnFindings` |
| [`lib/setup-assets.ps1`](lib/setup-assets.ps1) | Populate `<UserDataRoot>\native\` and `<UserDataRoot>\models\` with the whisper.cpp DLLs, MinGW C++ runtime, and Whisper / Silero VAD models. Idempotent. See *Native runtime* below for the three sourcing modes. | `-DataRoot <path>`, `-FromRelease X.Y.Z`, `-WhisperRepo <path>`, `-WithLarge`, `-Force` |
| [`lib/bootstrap-dev-env.ps1`](lib/bootstrap-dev-env.ps1) | Provision a fresh Windows 11 machine: winget (VS 2026, .NET 10, git, gh), optional scoop Tier 2 (MinGW, CMake, Ninja, Vulkan SDK, Ollama). Probes existing state, builds a plan, asks for confirmation, then executes. Runtime assets are left to the app's first-run wizard unless explicitly requested. | `-DryRun`, `-Full`, `-Yes`, `-IncludeAssets`, `-AssetsRelease X.Y.Z` |
| [`lib/record-version.ps1`](lib/record-version.ps1) | Frequent internal versioning path: bump through `cut-version.ps1`, refresh the generated `[Unreleased]` accumulator, and optionally push the branch. It creates no tag and no GitHub Release. `-Current` refreshes without another bump. | `-Target <worktree>`, `-Pick`, `-Bump patch\|minor\|major`, `-Current`, `-Push` |
| [`lib/cut-version.ps1`](lib/cut-version.ps1) | Low-level atomic bump: update `<Version>` in `Deckle.App.csproj` and commit only that one-line change as `chore(version): vX.Y.Z`. It does not tag, update the changelog, or push. | `-Target <worktree>`, `-Pick`, `-Bump patch\|minor\|major`, `-NoCommit` |
| [`lib/record-release.ps1`](lib/record-release.ps1) | Finalize a successful public release: add its existing tag to `release-history.json`, freeze `[Unreleased]` into the version section, commit both generated records, and optionally push the branch. Called by `publish-app.ps1`. | `-Target <worktree>`, `-Pick`, `-Version X.Y.Z`, `-Push` |
| [`lib/release-history.psm1`](lib/release-history.psm1) | Validate and update the ordered offline ledger of public GitHub release tags in `release-history.json`. Internal historical tags are not release boundaries. | internal module |
| [`lib/install-hooks.ps1`](lib/install-hooks.ps1) | Install the local git hooks sourced from `scripts/hooks/` into `.git/hooks/` and register the local `merge.ours` driver used by `TREE.md`. | |
| [`lib/update-readme-stats.ps1`](lib/update-readme-stats.ps1) | Regenerate the README `Development pulse` section from local Git history. `-Commit` creates a local README-only commit and refuses unrelated tracked changes; it never pushes. Also used without `-Commit` by the monthly GitHub Action. | `-Target <worktree>`, `-Pick`, `-ReadmePath <path>`, `-Commit` |
| [`lib/changelog.ps1`](lib/changelog.ps1) | Generate `CHANGELOG.md` and release notes from Conventional Commits and the public-release boundaries in `release-history.json`. Default preserves an `[Unreleased]` accumulator since the latest public release; `-NotesFor X.Y.Z` emits that full release range for GitHub. | `-Target <worktree>`, `-Pick`, `-NotesFor X.Y.Z`, `-OutFile <path>` |
| [`lib/publish-native-runtime.ps1`](lib/publish-native-runtime.ps1) | **Maintainer-only.** Package the native runtime zip (the C# DLL catalog + `PROVENANCE.txt` + `SHA256SUMS`) from an already-built local whisper.cpp tree, pair it with the MinGW runtime beside the compiler recorded by CMake, then optionally publish only `native-vX.Y.Z`. The version is inferred from whisper.cpp and existing native tags; `-Version` is a recovery override. No CMake or Deckle build is invoked. | `-WhisperRepo <path>`, `-Target <Deckle repo>`, `-OutDir <path>`, `-Publish`, `-Version X.Y.Z`, `-Notes <path>` |
| [`lib/action-summary.ps1`](lib/action-summary.ps1) | Internal shared writer for the final action summary used by worker scripts. | |
| [`lib/deckle-process.ps1`](lib/deckle-process.ps1) | Internal shared process guard: stop `Deckle.exe` and wait until no instance remains before build, launch, clean, or publish rewrites locked app artefacts. | |
| [`lib/_menu.psm1`](lib/_menu.psm1) | Public facade over `lib/menu/`, exposing the menu session, pickers, grid, compact status surface, and bounded single-line text input used by targeted paths. Pickers can clear the terminal before rendering so nested screens replace each other; the launcher can also use the terminal alternate screen buffer for a full-screen flow. Imported by `deckle.ps1`, `launch-app.ps1 -Pick`, `build-run.ps1 -Pick`, `clean.ps1 -Pick`, `stats.ps1 -Pick`, `update-readme-stats.ps1 -Pick`, `changelog.ps1 -Pick`. **Not an entry point.** |

## Git hooks — TREE.md auto-update

A `pre-commit` hook regenerates [`TREE.md`](../TREE.md) at the repo root before every commit and stages it automatically, so the repo always carries an up-to-date view of its tracked tree. Source in [`hooks/pre-commit`](hooks/pre-commit), local install via [`lib/install-hooks.ps1`](lib/install-hooks.ps1) or the `deckle.ps1` Setup menu to run once after a clone — hooks live under `.git/hooks/` and are not versioned by git.

The hook delegates to [`hooks/update-tree.ps1`](hooks/update-tree.ps1), which rebuilds `TREE.md` from `git ls-files` (flat view, zero gitignored file, no annotation). It can also run by hand to refresh outside a commit: `pwsh scripts/hooks/update-tree.ps1`.

## Generated docs automation

The root README carries a small generated `Development pulse` section bounded by invisible HTML comments. Regenerate it locally through the menu (`Update README pulse`) or directly:

```powershell
pwsh scripts/lib/update-readme-stats.ps1 -Commit
```

GitHub also runs `.github/workflows/update-readme-stats.yml` monthly and on manual dispatch. The workflow checks out full history (`fetch-depth: 0`), runs the same script, and commits `README.md` only when the generated section changed.

`CHANGELOG.md` is regenerated from local Git history, not hand-edited. `release-history.json` lists only real public GitHub releases and therefore supplies the changelog boundaries; old internal tags are ignored. `Record version` advances `<Version>` without a tag and refreshes `[Unreleased]` from the latest public release. `Publish app release` first requires a clean, synchronized `main` at a version newer than the ledger; it then builds and reopens the payload archive, uploads a GitHub draft, verifies the three remote assets, publishes the verified release, and only then records and pushes the public release locally. A retry reconciles an existing draft or public release from its downloaded artifacts without rebuilding or reuploading. A plain push has no changelog semantics. Regenerate the changelog alone through the menu (`Update changelog`) or directly:

```powershell
pwsh scripts/lib/changelog.ps1
```

## Native runtime — three sourcing modes

The app's first launch opens the in-app setup wizard when native DLLs or models are missing. The F5 menu also exposes `Set up runtime assets` as a developer shortcut over `lib/setup-assets.ps1`, which provisions the 8 native DLLs (5 whisper.cpp Vulkan + 3 MinGW C++ runtime) through one of three paths:

1. **`-FromRelease <X.Y.Z>` (default for non-rebuilders).** Fetches `deckle-native-<X.Y.Z>.zip` from the Deckle GitHub Release and extracts the catalog DLLs in place. No local whisper.cpp clone needed. Same source as the first-run wizard's auto-download path.

2. **`-WhisperRepo <path>` (for whisper.cpp rebuilders).** Copies DLLs from a local whisper.cpp build tree (`<path>\build\bin\` plus the MinGW runtime from Scoop). Use when iterating on whisper.cpp source — recompile, point the script at your tree, the bundle on `<UserDataRoot>` refreshes without going through GitHub. Falls back to `$env:DECKLE_WHISPER_REPO` and then to a sibling `<repo>\..\whisper.cpp` clone.

3. **Skip.** When neither path resolves to a valid build tree, the native step is skipped with a warning. Useful when only models need refreshing on a machine without a build tree.

The Whisper models are pulled from HuggingFace; the Silero VAD model is pulled from GitHub (snakers4/silero-vad, pinned to the v6.2 tag). Both happen regardless of native runtime sourcing mode.

## Post-build auto-relaunch

`lib/build-run.ps1` passes `--post-build` by default: the freshly launched app relaunches itself once so the HUD reliably claims topmost on the first recording. Disable with `-NoAutoRestart` when you need a stable PID (attached debugger, log capture).

## What is *not* here

- **MSIX / packaged installer.** No MSIX or Store package — Deckle ships *unpackaged*. The GitHub Release (a pre-release while on 0.x), cut by `lib/publish-app.ps1 -Publish`, carries two assets: the headline `Deckle-Setup-vX.Y.Z-win-x64.exe` — the installer stub the end user downloads and runs — and the self-contained app payload it fetches (`Deckle-vX.Y.Z.zip` + `.sha256`). Before building, the release command downloads and hash-verifies the native runtime pinned by the app, so it cannot publish an installer with a dead Whisper dependency. The `Deckle.Installer` stub resolves the latest release via the GitHub API, downloads the payload, sha256-verifies it, and installs per-user (GitHub auto-attaches the source-code archives too). Building from source via `lib/build-run.ps1` (or the launcher) stays the dev path.
- **Automated builds and releases.** There is no CI build or automated publishing. The only GitHub Action refreshes the generated README development pulse monthly; both release flows — the app ZIP and the native runtime — remain manual maintainer actions.
- **Source mirror of whisper.cpp.** The repo no longer carries a `whisper.cpp/` clone. Rebuilders clone it themselves alongside the Deckle repo, build it locally, and point `-WhisperRepo` at it.
