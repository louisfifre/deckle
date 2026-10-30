---
name: readme-scripts
description: "Dev workflows entry point for Deckle: the menu.ps1 launcher installed from Deckle Scripts with its packs, Deckle's own scripts laid out in the menu levels, the Header a script needs to appear, the versions as git tags, the git hooks and the three native-runtime sourcing modes. Read before running, modifying, or extending a script under scripts/."
module: scripts
---

# `scripts/` — Deckle dev workflows

Everything here runs under PowerShell 7. The entry point is [`menu.ps1`](menu.ps1), the launcher of [Deckle Scripts](https://github.com/louisfifre/deckle-scripts), installed here with four of its packs; Deckle's own scripts sit beside the installed ones, in the folders the menu shows as levels, and every one stays usable from a terminal or a `launch.json` profile for automation. Tracked workflows resolve the repository from their own location, so the checkout drive and the terminal's current directory are not part of their contract. Every script the menu offers carries a Header, the comment-based help block the launcher reads to build its menu: see *Adding a script* below before writing one.

The launcher's vocabulary (levels, actions, variants, executions, journals, results) is defined in the `GLOSSARY.md` of the deckle-scripts repository, not here.

## Entry point — `menu.ps1`

`menu.ps1` is what F5 runs in VSCodium (see [`.vscode/launch.json`](../.vscode/launch.json)) and what you call from a terminal for daily work. It is the only file Windows PowerShell 5.1 ever reads: it relaunches `lib/launcher/launcher.ps1` under `pwsh`, or says that PowerShell 7 must be installed. Interactive by default, it opens an arrow-key menu built at every launch from the scripts folder: the root holds the actions of the scripts at the root of `scripts/`, and each subfolder is a level with its own actions. A script whose Header carries a confirmation sentence opens that sentence as a step before running; the Danger treatment marks the actions that delete or replace something.

```powershell
pwsh -NoProfile -File scripts/menu.ps1
pwsh -NoProfile -File scripts/menu.ps1 -Palette TrueColor
```

Three switches serve a shell, an agent or a test without taking over the terminal:

```powershell
# Every action of the catalog, one per line: id, label, script, arguments, variant.
pwsh -NoProfile -File scripts/menu.ps1 -List

# One action by its id, without the menu; its journal is printed, its exit code says how it ended.
pwsh -NoProfile -File scripts/menu.ps1 -Run build-run.1 -Variant Debug
pwsh -NoProfile -File scripts/menu.ps1 -Run git.changelog.0

# One deterministic frame of the interface, for inspection and tests.
pwsh -NoProfile -File scripts/menu.ps1 -Snapshot Menu -Width 100 -Height 30
pwsh -NoProfile -File scripts/menu.ps1 -Snapshot Level -LevelPath maintenance -Width 100 -Height 30
pwsh -NoProfile -File scripts/menu.ps1 -Snapshot Confirmation
```

`-Snapshot` accepts `Menu`, `Level`, `Confirmation`, `Execution` and `Logs`. An agent inspects the interface through `-Snapshot` and `-List` only; the repository instructions forbid it to start the interactive launcher and to `-Run` an action that stops or relaunches the resident app.

What the menu offers today, level by level. The *Installed* column marks the scripts that come from Deckle Scripts; the others are Deckle's own.

| Level | Section | Action | Script | Installed |
|---|---|---|---|:---:|
| root | Run | Launch (Release / Debug) | [`launch-app.ps1`](launch-app.ps1) | |
|  |  | Build & run, Build (no run) (Release / Debug) | [`build-run.ps1`](build-run.ps1) | |
|  | Project | Update README pulse | [`update-readme-stats.ps1`](update-readme-stats.ps1) `-Commit` | |
|  | Checks | Check scripts | [`check-scripts.ps1`](check-scripts.ps1) | yes |
|  | (none, first row) | Check for updates, Update Deckle Scripts | [`update.ps1`](update.ps1) | yes |
| **Setup** | Repository | Preview scaffold, Scaffold repository files | [`setup/scaffold.ps1`](setup/scaffold.ps1) | yes |
|  |  | Preview hooks, Install git hooks | [`setup/install-hooks.ps1`](setup/install-hooks.ps1) | yes |
|  |  | Preview tree, Regenerate TREE.md | [`setup/update-tree.ps1`](setup/update-tree.ps1) | yes |
|  | Machine | Plan the bootstrap (dry run), Bootstrap dev environment | [`setup/bootstrap-dev-env.ps1`](setup/bootstrap-dev-env.ps1) | |
|  |  | Set up runtime assets | [`setup/setup-assets.ps1`](setup/setup-assets.ps1) | |
| **Git and versions** | Version | Record version (patch / minor / major) | [`git/record-version.ps1`](git/record-version.ps1) `-Apply` | yes |
|  | Release | Mark release | [`git/mark-release.ps1`](git/mark-release.ps1) `-Apply` | yes |
|  | Changelog | Preview changelog, Write changelog | [`git/changelog.ps1`](git/changelog.ps1) | yes |
| **Maintenance** | Statistics | Repository statistics | [`maintenance/stats.ps1`](maintenance/stats.ps1) | |
|  |  | Context inventory | [`maintenance/inspect-context.ps1`](maintenance/inspect-context.ps1) | |
|  | Cleanup | Preview build outputs, Clean build outputs | [`maintenance/clean.ps1`](maintenance/clean.ps1) | yes |
|  |  | Clean app build outputs | [`maintenance/clean-app.ps1`](maintenance/clean-app.ps1) | |
|  |  | Inspect native folder, Clean native folder | [`maintenance/clean-native.ps1`](maintenance/clean-native.ps1) | |
|  | AI sessions | Inspect AI sessions, Reset AI sessions | [`maintenance/reset-agent-state.ps1`](maintenance/reset-agent-state.ps1) | yes |
|  | Checks | Validate WinUI resources | [`maintenance/validate-resources.ps1`](maintenance/validate-resources.ps1) | |
|  |  | Sweep exposable source values | [`maintenance/sweep-exposables.ps1`](maintenance/sweep-exposables.ps1) | |
| **.NET** | | Stop .NET build servers | [`dotnet/stop-build-servers.ps1`](dotnet/stop-build-servers.ps1) | yes |
| **Release** | Release | Prepare app artifacts, Publish app release | [`release/publish-app.ps1`](release/publish-app.ps1) | |
|  |  | Prepare native runtime, Publish native runtime | [`release/publish-native-runtime.ps1`](release/publish-native-runtime.ps1) | |
|  |  | Build Anytype backend | [`release/build-anytype-backend.ps1`](release/build-anytype-backend.ps1) | |

A script with a `-Target` parameter opens the worktree Selector before running, so the same action serves the main checkout and every worktree. The menu asks nothing else: the choices a script needs are its Variants, declared in its Header.

## Installed from Deckle Scripts

The launcher (`menu.ps1`, `lib/launcher/`, `lib/terminal-interaction*`, `lib/script-discovery*`), the shared helpers it ships (`lib/action-summary.ps1`, `lib/native-console.ps1`, `lib/script-output.ps1`, `lib/build-outputs.ps1`, `lib/build-server-cleanup.ps1`, `lib/version.ps1`, `lib/manifest.ps1`, `lib/agent-state-maintenance*`, `lib/hooks/`, `lib/templates/`), `check-scripts.ps1`, `update.ps1` and the four packs `setup`, `git`, `maintenance` and `dotnet` are installed from the deckle-scripts repository at one of its version tags. [`.deckle-scripts/manifest.json`](.deckle-scripts/manifest.json) is the record of that install: the source, the version, the packs and every installed file with its hash.

Those files are never edited in this repository. An improvement, a fix or a new pack goes to the deckle-scripts repository, and comes back through an update:

```powershell
# Preview what the next version would change; nothing is written.
pwsh -NoProfile -File scripts/update.ps1
pwsh -NoProfile -File scripts/update.ps1 -Version v0.2.0

# Apply it. A file edited locally since its installation stops the update unless -Force.
pwsh -NoProfile -File scripts/update.ps1 -Apply
```

The update never overwrites a file it does not own, so Deckle's own scripts, library modules and tests sit safely beside the installed ones. Updating is the maintainer's act, never an agent's.

## Deckle's own scripts

Each script is callable directly from a terminal; the menu is purely additive. Concrete scripts end with the same human-readable action summary: a short sentence, a `Workflow`, a `Result` (`Success`, `Failed`, `Partial`, or `Skipped`), then the fields that matter for that workflow. The detailed step logs stay above it for diagnosis.

| File | Purpose | Common switches |
|---|---|---|
| [`launch-app.ps1`](launch-app.ps1) | Kill running `Deckle.exe` and launch the freshest already-built app executable. Does not build. | `-Configuration Debug\|Release`, `-Target <worktree>`, `-Wait` |
| [`build-run.ps1`](build-run.ps1) | Kill running `Deckle.exe`, build via `dotnet build` on four low-priority MSBuild nodes that exit with the build, and launch the freshly built exe through ShellExecute. With `-NoRun`, build only: the running instance is never stopped, and the build is refused before compiling when that instance runs from the build's own output directory (see *Running instance and build outputs*). | `-Configuration Debug\|Release`, `-NoRun`, `-Wait`, `-Target <worktree>`, `-NoAutoRestart` |
| [`update-readme-stats.ps1`](update-readme-stats.ps1) | Regenerate the README `Development pulse` section from local Git history. `-Commit` creates a local README-only commit and refuses unrelated tracked changes; it never pushes. Also used without `-Commit` by the monthly GitHub Action. | `-Target <worktree>`, `-ReadmePath <path>`, `-Commit` |
| [`maintenance/clean-app.ps1`](maintenance/clean-app.ps1) | Kill running `Deckle.exe` (it locks the output), stop .NET build servers left by manual/agent builds, then remove the consolidated `artifacts/{bin,obj,publish,package}/` plus any straggler `bin/`+`obj/` under `src/`, `tests/`, and benchmark study folders. Keeps `artifacts/Deckle-v*` release staging unless `-IncludeReleases`. Guards against symlinks / junctions. Reports total freed bytes. The pack's `clean.ps1` beside it knows nothing of Deckle's layout, so this one stays the cleanup to use here. | `-Target <worktree>`, `-IncludeReleases` |
| [`maintenance/clean-native.ps1`](maintenance/clean-native.ps1) | Bring `<UserDataRoot>\native\` back to the catalog of `NativeRuntime.cs`: delete the DLLs it does not name (an older bundle's `libwhisper.dll` and MinGW runtime) and `.partial` leftovers, then report the catalog DLLs that are missing. Does not stop Deckle; a DLL in use is reported and kept. | `-DataRoot <path>`, `-WhatIf` |
| [`maintenance/stats.ps1`](maintenance/stats.ps1) | Walk every `.csproj` under `src/`, build a per-file inventory, highlight C# files over 400 / 600 effective LOC and other text files over 500 / 1000 raw lines, summarize modules by source LOC, list tracked repository file types dynamically, and print the per-file module table. Git defines the repository boundary, so ignored workspace data is excluded and tracked links are counted without traversal. Generated module files are also excluded. | `-Target <worktree>`, `-Json <path>`; `-PassThru` for structured output |
| [`maintenance/inspect-context.ps1`](maintenance/inspect-context.ps1) | Inventory tracked Markdown documents by loading mode, Git modification date, additions over 1/7/30 days, heading count, exact size and line count, plus a model-independent token estimate. Separates automatically scoped instructions from references that must be opened explicitly. | `-Target <worktree>`, `-RelativePath <path>`, `-LoadingMode <name[]>`, `-DocumentType <name[]>`, `-SkipActivity`, `-Json <path>`; `-PassThru` includes documents |
| [`maintenance/sweep-exposables.ps1`](maintenance/sweep-exposables.ps1) | Inventory tracked source values that may deserve configuration or design arbitration without building or loading Deckle. Writes one JSON object per line with its source location. | `-RepoRoot <worktree>`, `-OutputPath <path>`, `-Quiet` |
| [`maintenance/validate-resources.ps1`](maintenance/validate-resources.ps1) | Audit every module `Resources.resw` against static `x:Uid`, `Loc.Get*`, descriptor literals, and the explicit dynamic-key allowlist ([`resource-validation.allowlist.json`](resource-validation.allowlist.json)). Reports missing keys, required-mirror divergences, and potentially unused copies without modifying resources. | `-Target <worktree>`, `-Allowlist <path>`, `-Json <path>`, `-FailOnFindings` |
| [`setup/setup-assets.ps1`](setup/setup-assets.ps1) | Populate `<UserDataRoot>\native\` and `<UserDataRoot>\models\` with the whisper.cpp DLLs, the Visual C++ runtime, and Whisper / Silero VAD models. Idempotent. See *Native runtime* below for the three sourcing modes. | `-DataRoot <path>`, `-FromRelease X.Y.Z`, `-WhisperRepo <path>`, `-WhisperBuildDir <name>`, `-WithLarge`, `-Force` |
| [`setup/bootstrap-dev-env.ps1`](setup/bootstrap-dev-env.ps1) | Provision a fresh Windows 11 machine: winget (VS 2026, .NET 10, git, gh), optional scoop Tier 2 (CMake, Ninja, Vulkan SDK, MinGW for the Anytype backend, Ollama). Probes existing state, builds a plan, asks for confirmation, then executes. Runtime assets are left to the app's first-run wizard unless explicitly requested. | `-DryRun`, `-Full`, `-Yes`, `-IncludeAssets`, `-AssetsRelease X.Y.Z` |
| [`release/publish-app.ps1`](release/publish-app.ps1) | Prepare or publish the app release at the version the git tag on HEAD names: build the installer stub and the self-contained app ZIP with its sha256 sidecar. Without `-Publish` both are built locally for inspection; with `-Publish` they are also uploaded as a GitHub Release under the version tag via `gh`, then the release mark is created and the changelog regenerated (see *Versions and changelog*). | `-Target <worktree>`, `-OutDir <path>`, `-Publish`, `-Notes <file>` |
| [`release/publish-native-runtime.ps1`](release/publish-native-runtime.ps1) | **Maintainer-only.** Validate and optionally publish the bundle pinned by `NativeRuntime.CurrentBundle`, auto-discovered under `artifacts/` or supplied with `-ArtifactPath`. When no current bundle exists, package a new one from an already-built local whisper.cpp tree (MSVC, dynamic ggml backends) and the Visual C++ Redist of the same Visual Studio. Every bundle, staged or downloaded, is refused when a DLL imports a name that is neither in the bundle nor part of Windows (`vulkan-1.dll` is the one declared external). Local and downloaded release assets are verified by size, archive contract, and SHA-256. No CMake or Deckle build is invoked. | `-ArtifactPath <zip>` or `-WhisperRepo <path>` with `-WhisperBuildDir <name>`; `-Target <Deckle repo>`, `-OutDir <path>`, `-Publish`, `-Version X.Y.Z`, `-Notes <path>` |
| [`release/build-anytype-backend.ps1`](release/build-anytype-backend.ps1) | Rebuild one `anytype-cli` release tag from source for Windows, UCRT-linked, with the tantivy-go cross-allocator free patched in a private module copy, then package it in the official asset layout with a sha256 sidecar and a build report under `artifacts/anytype-backend-vX.Y.Z/`. The patch applies nothing once no site matches and refuses a partial match. See the script's own comments for the why (anyproto/tantivy-go#25, anyproto/anytype-cli#63). | `-Version X.Y.Z`, `-OutDir <path>`, `-Force` |

Three scripts carry no Header and stay out of the menu on purpose:

| File | Purpose | Common switches |
|---|---|---|
| [`build-locked.ps1`](build-locked.ps1) | Run one `dotnet build` or `dotnet test` under the machine-wide `Global\Deckle.Build` mutex, with the project's mandatory flags, so parallel agents never build at once. `-Project` is mandatory, which the menu could not supply. | `-Project <path>`, `-Test`, `-Configuration Debug\|Release` |
| [`fetch-autocorrect-data.ps1`](fetch-autocorrect-data.ps1) | Stage 0 of the autocorrect pipeline: download the raw lexical sources (Lexique 3.83, Norvig counts, FranceTerme, a Wikipedia FR train/eval corpus) into `artifacts\autocorrect-data\raw\`. Acquires data only, idempotent, never versioned. | `-OutputRoot <path>`, `-TrainMB`, `-EvalMB`, `-IncludeKaikki`, `-Force` |
| [`maintenance/survey-anytype-v2.ps1`](maintenance/survey-anytype-v2.ps1) | Replay a fixed list of read-only measures against the local headless Anytype backend and write one Markdown report, so the v2 port of `Deckle.Anytype` is decided on measured behaviour rather than on contradictory docs. Rerun on every new backend version and diff the reports; the safety contract is in the script's own comments. | `-OutputPath <path>` |

## Adding a script — the Header

The menu is built at every launch from the scripts folder by `lib/script-discovery.psm1`; no catalog is written by hand. Dropping a script into `scripts/`, or into one of its subfolders, and giving it a Header is the whole gesture, for a person and for an agent alike. The Header is PowerShell's own comment-based help, read through the parser without running the script:

```powershell
<#
.SYNOPSIS
    Build the app
.DESCRIPTION
    Build the chosen configuration on four short-lived MSBuild nodes, then
    launch the freshly built executable.
.NOTES
    section: Run
    order: 120
    action: Build & run |
    action: Build (no run) | -NoRun
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Target
)
```

What each part does:

- The block is first in the file, `#Requires` comes after it, and one blank line follows `#>`. A `#Requires` line or a comment glued directly above the block voids it silently.
- `.SYNOPSIS` is the label the menu shows, one line. `.DESCRIPTION` is its help sentence. `TREE.md` takes them as the script's name and description.
- `.NOTES` carries the menu keys as `key: value` lines, the same syntax as the front matter of the Markdown files: `section` groups the script with others at the same level, `order` ranks it within the level, and each `action:` line declares one Action. Sections sort by the smallest `order` they hold, so the orders are numbered across the sections of a level and the hundreds digit ranks the section: at the root, Run 1xx and Project 2xx. Without an `action:` line the script declares one Action, its synopsis as label, no arguments.
- An `action:` line reads `Label | arguments | danger | sentence`. The label comes first, then the arguments appended verbatim to the invocation (may be empty), then in any order the word `danger`, which gives the Danger treatment, and a sentence, which makes the Confirmation a separate step naming that sentence.
- The Variants of every Action come from the script's single `[ValidateSet]` parameter of scalar type, in declaration order, the default preselected; activating a Variant passes that value. A `[ValidateSet]` on an array parameter is a Filter, not an axis. A script that needs two axes is two scripts. A `-Target` parameter opens the worktree Selector in the launcher.
- Inside the block no line may start with a dot followed by letters except a help keyword, and no keyword may be invented: the block dies silently, with no error.

A subfolder of `scripts/` is a menu level, except `lib`, `tests` and the dot-folders: an Access labelled by its name, or by the `name` of its `README.md` front matter, which may also carry `description` and `order`. The packs bring their own level READMEs (`setup`, `git`, `maintenance`, `dotnet`); Deckle's `release/README.md` is the one level of its own. Scripts at the root of `scripts/` sit on the root menu. Within a level, Action Rows come first grouped by `section`, then the subfolders as one group. Inside a section the Actions come in three bands: scripts with Variants first, then plain Actions, then Danger Actions last; `order` sorts inside each band.

A script without a Header stays out of the menu on purpose, so the folder can keep a script that served once. [`check-scripts.ps1`](check-scripts.ps1) lists the hidden scripts and fails on a Header that is present but voided, on a `-Pick` parameter (the menu asks, a script never does), on a `-Target` that is not a plain string and on a file encoded with a byte order mark; run it before closing a change that touches a script, and `tests/lib/script-layout.tests.ps1` enforces the same rule.

## Library — `lib/`

Nothing under `lib/` is a user command. Most of it is installed (see *Installed from Deckle Scripts*): the launcher, the terminal interaction framework, Script Discovery, the shared output helpers and the hook sources. Deckle's own modules sit beside them:

| File | Purpose |
|---|---|
| [`lib/deckle-process.ps1`](lib/deckle-process.ps1) | Shared process guard for scripts that replace locked app artifacts. |
| [`lib/repository-inventory.psm1`](lib/repository-inventory.psm1) | Reusable tracked-file inventory for targeted scans. |
| [`lib/source-metrics.psm1`](lib/source-metrics.psm1) | Source metrics (effective C# lines) shared by the repository inventory and its focused tests. |
| [`lib/context-inventory.psm1`](lib/context-inventory.psm1) | Context statistics: classifies tracked Markdown documents by loading mode and type. |
| [`lib/resource-inventory.psm1`](lib/resource-inventory.psm1) | Static, read-only inventory of WinUI resource references and `.resw` maps. |
| [`lib/native-runtime-release.psm1`](lib/native-runtime-release.psm1) | Reads and validates the native runtime artifact pinned by the app; app publication uses it as a fail-closed preflight. |
| [`lib/pe-imports.psm1`](lib/pe-imports.psm1) | Reads the import table of a PE file, so a bundle check can refuse a DLL that imports a name neither in the bundle nor part of Windows. |
| [`lib/release-validation.psm1`](lib/release-validation.psm1) | Release publication invariants shared by `publish-app.ps1` and its tests, including the release marks; every assertion fails closed. |

## Tests — `tests/`

PowerShell tests live under `tests/lib/` for the modules above and `tests/commands/` for the scripts whose behaviour deserves a suite of their own, plus [`tests/BugExamples/`](tests/BugExamples/) for the bug notes the script tests own. The installed launcher and packs are tested in the deckle-scripts repository, not here. Run a suite with `pwsh -File scripts/tests/lib/<name>.tests.ps1`; `script-layout.tests.ps1` checks the folder layout and the Headers of the whole tree.

## Git hooks — TREE.md and commit identity

Two repository hooks come from the setup pack, installed into `.git/hooks/` by [`setup/install-hooks.ps1`](setup/install-hooks.ps1) `-Apply` (or the menu's *Install git hooks*), to run once after a clone since installed hooks are not versioned. An existing hook that differs is kept aside as a `.bak` file, and the installer also registers the `TREE.md` merge driver.

The `pre-commit` hook regenerates [`TREE.md`](../TREE.md) at the repo root before every commit and stages it automatically, so the repo always carries an up-to-date view of its tracked tree. It delegates to [`setup/update-tree.ps1`](setup/update-tree.ps1), which rebuilds `TREE.md` from `git ls-files`: tracked files only, each Markdown file annotated from its front matter and each script from its Header, through the same discovery module as the menu. It can also run by hand to refresh outside a commit: `pwsh scripts/setup/update-tree.ps1 -Apply`.

Until Deckle Scripts resolves the repository root from inside a hook ([louisfifre/deckle-scripts#1](https://github.com/louisfifre/deckle-scripts/issues/1): git exports `GIT_DIR` there, so `update-tree.ps1` called without `-Target` takes its own folder for the root), install only the identity guard, `install-hooks.ps1 -Apply -Hooks commit-msg`, and keep the transitional `pre-commit` already in `.git/hooks/`, which passes `-Target` itself.

The `commit-msg` hook runs `lib/hooks/validate-commit-attribution.ps1`: before Git creates a commit, it requires both author and committer to be the identity git resolves for the repository and rejects co-author, generated-by, AI-generated, and assisted-by markers. It is repository-local; the machine-wide configured hook installed by the previous launcher, if still present, keeps applying on its own.

## Versions and changelog

The version is a git tag, no file carries the number. An annotated `vX.Y.Z` tag is recorded on the commit that enters main at each merge, by the git pack:

```powershell
pwsh scripts/git/record-version.ps1 -Bump patch        # preview
pwsh scripts/git/record-version.ps1 -Bump minor -Apply # tag HEAD
```

A build reads the highest version tag reachable from its HEAD, through the `ReadVersionFromGit` target of `src/Deckle.App/Deckle.App.csproj`, and refuses to build without one, so a fresh clone runs `git fetch --tags` first. A public release is marked by a second annotated tag, `release/vX.Y.Z`, on the released commit: `release/publish-app.ps1 -Publish` creates it once the GitHub release is public, then regenerates and commits `CHANGELOG.md`; `git/mark-release.ps1 -Apply` creates the same mark by hand.

`CHANGELOG.md` is regenerated from local Git history by the git pack, not hand-edited: the release marks are its section boundaries and `[Unreleased]` holds what came since the latest mark. `Publish app release` first requires a clean, synchronized `main` at a tagged version with no release mark yet; it then builds and reopens the payload archive, uploads a GitHub draft, verifies the three remote assets, publishes the verified release, and only then marks and records the public release locally. A retry reconciles an existing draft or public release from its downloaded artifacts without rebuilding or reuploading. A plain push has no changelog semantics.

```powershell
pwsh scripts/git/changelog.ps1          # preview
pwsh scripts/git/changelog.ps1 -Apply   # write CHANGELOG.md
```

The bump rules, the categories and the release-notes path are the `deckle-versioning` skill's.

## Generated docs automation

The root README carries a small generated `Development pulse` section bounded by invisible HTML comments. Regenerate it locally through the menu (`Update README pulse`) or directly:

```powershell
pwsh scripts/update-readme-stats.ps1 -Commit
```

GitHub also runs `.github/workflows/update-readme-stats.yml` monthly and on manual dispatch. The workflow checks out full history (`fetch-depth: 0`), runs the same script, and commits `README.md` only when the generated section changed.

## Native runtime — three sourcing modes

The app's first launch opens the in-app setup wizard when native DLLs or models are missing. The menu also exposes `Set up runtime assets` as a developer shortcut over `setup/setup-assets.ps1`, which provisions the native DLL catalog of `NativeRuntime.cs` (whisper.cpp with its dynamic ggml backends, one per CPU level plus Vulkan, and the 4 Visual C++ runtime DLLs) through one of three paths:

1. **`-FromRelease <X.Y.Z>` (default for non-rebuilders).** Fetches `deckle-native-<X.Y.Z>.zip` from the Deckle GitHub Release and extracts the catalog DLLs in place. No local whisper.cpp clone needed. Same source as the first-run wizard's auto-download path.

2. **`-WhisperRepo <path>` (for whisper.cpp rebuilders).** Copies DLLs from a local whisper.cpp build tree built with MSVC (`<path>\<build>\bin\`, `-WhisperBuildDir` naming the build folder) plus the Visual C++ Redist of the Visual Studio that compiled it; a MinGW tree is refused. Use when iterating on whisper.cpp source — recompile, point the script at your tree, the bundle on `<UserDataRoot>` refreshes without going through GitHub. Falls back to `$env:DECKLE_WHISPER_REPO` and then to a sibling `<repo>\..\whisper.cpp` clone.

3. **Skip.** When neither path resolves to a valid build tree, the native step is skipped with a warning. Useful when only models need refreshing on a machine without a build tree.

Native release publication is separate from runtime setup. Its ordinary path reuses the exact bundle already pinned by the app at `artifacts/deckle-native-<version>/deckle-native-<version>.zip`; the script does not ask for a whisper.cpp path when that file exists. The source-tree path is only the exceptional path for fabricating a new runtime version. Publication re-downloads the GitHub asset and verifies its full SHA-256 and internal DLL checksums before making the release public.

The Whisper models are pulled from HuggingFace; the Silero VAD model is pulled from GitHub (snakers4/silero-vad, pinned to the v6.2 tag). Both happen regardless of native runtime sourcing mode.

## Running instance and build outputs

Deckle is single-instance (a resident mutex rejects a second process) and holds its executable plus every loaded assembly open for as long as it runs. Two consequences shape the scripts that touch it. Scripts that *replace* the running instance — `launch-app.ps1`, `build-run.ps1` without `-NoRun`, and `maintenance/clean-app.ps1`, which deletes its files — stop it first; that is their contract, stated in each script. Scripts that only *build* must not: `build-run.ps1 -NoRun` leaves the running instance alone, whatever worktree or configuration it was launched from. The single case where that instance would block the build is a build into the very directory it runs from (`artifacts\bin\Deckle.App\<configuration>` of the same checkout): the script then refuses before compiling and names the PID, the directory, and the ways out — build Debug, build from another worktree, or drop `-NoRun` to replace the instance — rather than failing on locked files after a full compile and leaving the output half-copied. Agent validation builds (Debug x64, see the repository instructions) therefore never conflict with a Release instance.

## Post-build auto-relaunch

`build-run.ps1` passes `--post-build` by default: the freshly launched app relaunches itself once so the HUD reliably claims topmost on the first recording. Disable with `-NoAutoRestart` when you need a stable PID (attached debugger, log capture).

## What is *not* here

- **MSIX / packaged installer.** No MSIX or Store package — Deckle ships *unpackaged*. The GitHub Release, cut by `release/publish-app.ps1 -Publish`, carries two assets: the headline `Deckle-Setup-vX.Y.Z-win-x64.exe` — the installer stub the end user downloads and runs — and the self-contained app payload it fetches (`Deckle-vX.Y.Z.zip` + `.sha256`). Before building, the release script downloads and hash-verifies the native runtime pinned by the app, so it cannot publish an installer with a dead Whisper dependency. The `Deckle.Installer` stub resolves the latest release via the GitHub API, downloads the payload, sha256-verifies it, and installs per-user (GitHub auto-attaches the source-code archives too). Building from source via `build-run.ps1` (or the menu) stays the dev path.
- **Automated builds and releases.** There is no CI build or automated publishing. The only GitHub Action refreshes the generated README development pulse monthly; both release flows — the app ZIP and the native runtime — remain manual maintainer actions.
- **The launcher's own code and docs.** Its glossary, its interaction specification, its tests and its history live in the deckle-scripts repository; here it is an installed dependency.
- **Source mirror of whisper.cpp.** The repo no longer carries a `whisper.cpp/` clone. Rebuilders clone it themselves alongside the Deckle repo, build it locally, and point `-WhisperRepo` at it.
