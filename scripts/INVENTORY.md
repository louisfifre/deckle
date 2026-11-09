# Scripts

_Generated from the scripts' Headers by `scripts/setup/update-inventory.ps1` at each commit — do not edit by hand._

Run one with `pwsh -NoProfile -File scripts/<path> [arguments]`. An action names the arguments the menu passes; one marked danger changes or deletes something, so read its script first. When no script fits, use your own command.

## `scripts/`

- `build-locked.ps1` — Run one dotnet build or test under the machine-wide Deckle build lock. (not in the menu)
  - Parameters: -Project <string> (required), -Test, -Configuration <string>, -DotnetArgs <string[]>
- `build-run.ps1` — **Build the app.** Build Deckle for the chosen configuration (x64) and, unless told not to, launch the fresh build.
  - Actions: Build & run · Build (no run) `-NoRun`
  - Parameters: -NoRun, -Wait, -Configuration Release|Debug, -Target <string>, -NoAutoRestart, -HudZOrderSelfTest
- `build-server-watchdog.ps1` — Shut the .NET build servers down after five idle minutes. (not in the menu)
  - Parameters: -StateDir <string>, -Arm, -Run, -IdleMinutes <int>, -TickSeconds <int>
- `deploy-prod.ps1` — **Update the prod copy.** Build main in Release in a build worktree of its own, check it with the self-check, and stage it in the prod copy, the Deckle the maintainer runs every day.
  - Actions: Update prod · Roll back prod `-Rollback`
  - Parameters: -Ref <string>, -Root <string>, -Rollback, -Force, -SkipSelfCheck
- `fetch-autocorrect-data.ps1` — Downloads the raw lexical sources for the French diacritics autocorrect into <OutputRoot>\raw\. (not in the menu)
  - Parameters: -OutputRoot <string>, -TrainMB <double>, -EvalMB <double>, -IncludeKaikki, -Force
- `inspect-context.ps1` — **Context inventory.** Inventory the repository's tracked Markdown context, grouped by loading mode, with line counts and recent additions.
  - Actions: Statistics / context
  - Parameters: -Target <string>, -RelativePath <string>, -LoadingMode <string[]>, -DocumentType <string[]>, -SkipActivity, -Json <string>, -PassThru
- `inspect-processes.ps1` — **Show running builds and Deckle instances.** Lists, read-only, the Deckle instances running on this machine with the folder each runs from, then the dotnet builds and tests in progress with their project and age, the build scripts waiting for a slot, and the .NET build servers left behind.
  - Actions: Inspect / processes
- `launch-app.ps1` — **Launch the freshest built app.** Replace the running Deckle instance with the freshest already-built executable of the chosen configuration.
  - Actions: Launch
  - Parameters: -Configuration Release|Debug, -Target <string>, -Wait
- `menu.ps1` — Entry of the repository's terminal menu. (not in the menu)
- `read-log.ps1` — **Read Deckle's log.** Prints the latest entries of Deckle's diagnostics log, one line each: time, level, provider and event, message.
  - Actions: Inspect / log · Inspect / trace `-Trace`
  - Parameters: -Trace, -Last <int>, -Level <string>, -SinceMinutes <int>, -Match <string>, -Payload, -DataRoot <string>
- `self-check.ps1` — **Self-check the built app.** Runs the built Deckle once in its self-check mode and judges its report.
  - Actions: Self-check
  - Parameters: -Configuration Debug|Release, -Target <string>, -TimeoutSeconds <int>, -OutputFolder <string>, -LinkRuntime, -KeepData
- `stats.ps1` — **Repository statistics.** Walk every project under src\ and build one file inventory per Deckle module, with line counts.
  - Actions: Statistics / repository
  - Parameters: -Target <string>, -WatchThreshold <int>, -TooLargeThreshold <int>, -CSharpWatchThreshold <int>, -CSharpTooLargeThreshold <int>, -ResourceWatchThreshold <int>, -ResourceTooLargeThreshold <int>, -Json <string>, -PassThru
- `update-readme-stats.ps1` — **Update the README pulse.** Regenerate the small "Development pulse" section in README.md from the local Git history.
  - Actions: README pulse / update `-Commit`
  - Parameters: -Target <string>, -ReadmePath <string>, -Commit

## `scripts/git/`

- `git/changelog.ps1` — **Update the changelog.** Generates CHANGELOG.md in the Keep a Changelog format from the Conventional Commits history, one section per published version.
  - Actions: Changelog / preview · Changelog / write `-Apply`
  - Parameters: -Target <string>, -Apply, -NotesFor <string>, -OutFile <string>, -Floor <string>
- `git/git-common.ps1` — Steps shared by the scripts of the git pack, dot-sourced by each of them. (not in the menu)
- `git/mark-release.ps1` — Mark a release. (not in the menu)
  - Parameters: -Target <string>, -Apply, -Push
- `git/record-version.ps1` — **Record a version.** Records the next version as an annotated tag vX.Y.Z on HEAD, bumping the latest version tag of the repository.
  - Actions: Record version `-Apply`
  - Parameters: -Bump patch|minor|major, -Target <string>, -Apply, -Push

## `scripts/maintenance/`

- `maintenance/clean-native.ps1` — **Clean the native folder.** Brings the whisper.cpp native folder under the user data root back to the current catalog, the RequiredDllNames of NativeRuntime.cs: deletes every DLL the catalog does not name (an older bundle's libwhisper.dll and MinGW runtime, a stray build artifact) and the .partial leftovers of an interrupted extraction, then reports the catalog DLLs that are missing.
  - Actions: Preview / native folder `-WhatIf` · Clean / native folder (danger)
  - Parameters: -DataRoot <string>
- `maintenance/clean.ps1` — **Clean build outputs.** Removes the build output folders of every language present in the repository, each only when it passes three locks: it is a known output folder of a language the repository uses, git ignores it, and it holds no tracked file.
  - Actions: Preview / build outputs · Clean / build outputs `-Apply` (danger)
  - Parameters: -Target <string>, -Apply
- `maintenance/reset-agent-state.ps1` — **Inspect or reset local AI sessions.** Builds a cleanup plan for the local Claude Code session data of the current account, which belongs to the user and not to a repository.
  - Actions: Preview / AI sessions · Clean / AI sessions `-Apply -Confirmation 'RESET LOCAL AI SESSIONS'` (danger)
  - Parameters: -Scope <string[]>, -Apply, -Confirmation <string>
- `maintenance/stop-build-servers.ps1` — **Stop .NET build servers.** Stops the MSBuild and Roslyn build servers left behind by dotnet builds, which Task Manager lists as ".NET Host" and "VBCSCompiler".
  - Actions: Clean / .NET build servers (danger)
- `maintenance/sweep-exposables.ps1` — **Sweep exposable source values.** Text sweep of the exposables carried by the source tree.
  - Actions: Check / exposable values
  - Parameters: -RepoRoot <string>, -OutputPath <string>, -Quiet
- `maintenance/validate-resources.ps1` — **Validate WinUI resources.** Read-only audit of the WinUI resource inventories.
  - Actions: Check / WinUI resources
  - Parameters: -Target <string>, -Allowlist <string>, -Json <string>, -MaxItems <int>, -FailOnFindings

## `scripts/release/`

- `release/build-anytype-backend.ps1` — **Build the Anytype backend.** Rebuilds one anytype-cli release tag from source for Windows with the tantivy-go cross-allocator free fixed, then packages it in the layout of the official asset.
  - Actions: Anytype backend / build
  - Parameters: -Version <string>, -OutDir <string>, -Force
- `release/publish-app.ps1` — **Prepare or publish the app release.** Builds the two artifacts a Deckle GitHub Release carries: the installer stub and the self-contained app ZIP with its sha256 sidecar.
  - Actions: App release / prepare · App release / publish `-Publish` (danger)
  - Parameters: -Target <string>, -OutDir <string>, -Publish, -Notes <string>
- `release/publish-native-runtime.ps1` — **Prepare or publish the native runtime.** Packages a versioned zip of the native DLLs that Deckle's first-run wizard downloads at install time.
  - Actions: Native runtime / prepare · Native runtime / publish `-Publish` (danger)
  - Parameters: -Version <string>, -WhisperRepo <string>, -WhisperBuildDir <string>, -ArtifactPath <string>, -Target <string>, -OutDir <string>, -Publish, -Notes <string>

## `scripts/settings/`

- `settings/check-scripts.ps1` — **Check scripts.** Reads every script the menu could offer and reports the ones it would hide or that break the conventions: a voided, invalid or unparsable Header (PowerShell drops such a Header without any error), a -Pick parameter (the menu asks the questions), a -Target parameter that is not a string, or a file encoded with a byte order mark.
  - Actions: Scripts / check
  - Parameters: -Target <string>
- `settings/update.ps1` — **Update Deckle Scripts.** Installs or updates the launcher, its library and the chosen packs from a source repository at a version tag.
  - Actions: Deckle Scripts / check updates · Deckle Scripts / update `-Apply`
  - Parameters: -Target <string>, -Source <string>, -Version <string>, -Packs <string[]>, -Apply, -Force

## `scripts/setup/`

- `setup/bootstrap-dev-env.ps1` — **Bootstrap the dev environment.** Brings a fresh Windows 11 machine up to speed for building and running Deckle.
  - Actions: Dev environment / plan `-DryRun` · Dev environment / bootstrap `-Yes` (danger)
  - Parameters: -DryRun, -Full, -IncludeAssets, -SkipAssets, -AssetsRelease <string>, -Yes
- `setup/build-portaudio.ps1` — **Build PortAudio.** Rebuilds the portaudio.dll that Deckle.Audio ships, from the pinned PortAudio commit, with the WASAPI host API only, and writes it with its license and provenance into src/Deckle.Audio/Native/win-x64.
  - Actions: PortAudio / build
  - Parameters: -WorkDir <string>
- `setup/install-hooks.ps1` — **Install git hooks.** Installs the repository's git hooks from their versioned sources and registers the TREE.md merge driver.
  - Actions: Git hooks / preview · Git hooks / install `-Apply`
  - Parameters: -Target <string>, -Apply, -Hooks <string[]>
- `setup/scaffold.ps1` — **Scaffold repository files.** Creates the standard folders and files a repository starts with, and never overwrites what exists: whatever is already there is kept as it is.
  - Actions: Scaffold / preview · Scaffold / apply `-Apply`
  - Parameters: -Target <string>, -Apply, -Folders <string[]>, -License <string>, -Name <string>, -Holder <string>
- `setup/setup-assets.ps1` — **Set up runtime assets.** Populates the native and models folders under the user data root with the whisper.cpp DLLs, the Visual C++ runtime they import and the Whisper models the app needs at runtime.
  - Actions: Runtime assets / set up
  - Parameters: -DataRoot <string>, -FromRelease <string>, -WhisperRepo <string>, -WhisperBuildDir <string>, -WithLarge, -Force
- `setup/update-inventory.ps1` — Regenerate scripts/INVENTORY.md. (not in the menu)
  - Parameters: -Target <string>, -Apply
- `setup/update-tree.ps1` — Regenerate TREE.md. (not in the menu)
  - Parameters: -Target <string>, -Apply
