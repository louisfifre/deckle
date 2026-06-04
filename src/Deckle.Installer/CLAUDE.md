# CLAUDE.md — Deckle.Installer

The download stub that brings Deckle onto an end user's PC. A standalone console
`.exe`, **NativeAOT**, distributed on its own — not part of the app composition and
not referenced by any other module. Double-click → a coloured console flow (the
feel of the repo's PowerShell scripts, compiled in), no GUI.

## Boundary — installer vs `Deckle.Setup`

Two different objects, deliberately kept apart:

- **This module** is the *installer*: it places the app, integrates it, launches it.
  Minimal, shipped first.
- **`Deckle.Setup`** is the in-app *first-run wizard* (and the future rich,
  VS-Installer-style module-management surface). It provisions the whisper.cpp
  native runtime and the speech models, per user, on first launch.

The installer never touches natives, models, or Ollama. Its contract is narrow:
**download the app payload → verify → install → integrate → launch.** Everything
runtime-provisioning is the app's own first-run job.

## What it does — the seven-step flow

1. System check (64-bit Windows) + folder choices.
2. Resolve the latest release from GitHub.
3. Download the payload (the heavy step; the only progress bar, byte-driven).
4. Verify SHA-256 against the published sidecar.
5. Extract into the install folder; copy itself in as the uninstaller.
6. Integrate: Start Menu shortcut, Installed-apps entry, `DECKLE_DATA_ROOT` if the
   data folder is non-default.
7. Launch `Deckle.exe`.

Two folders, the install UX's whole point: **binaries** (`%LOCALAPPDATA%\Programs\Deckle`
by default, per-user, no admin) and **data/models** (default `%LOCALAPPDATA%\Deckle`,
relocatable off a saturated C: via `DECKLE_DATA_ROOT`). `C:\Program Files` is avoided
(forces C: + elevation).

`--uninstall` (the registered UninstallString) reverses it: deregister, optionally
drop the data folder (preserved by default — re-downloading 3 GB of models is not a
silent act), then a detached `cmd` removes the binaries including the running exe.

CLI: no args = interactive. `--install-dir`, `--data-dir`, `-y/--yes` (accept
defaults, no prompts), `--uninstall`.

## Build and publish

Compile validation is `dotnet build` (IL only — the AOT analyzers still run, so
non-AOT-safe code surfaces here):

```
dotnet build src/Deckle.Installer/Deckle.Installer.csproj -c Debug -p:Platform=x64
```

The native exe is produced by **publish — the maintainer's act**, which is also the
only step that exercises the NativeAOT link (needs the MSVC toolchain, present on
this machine):

```
dotnet publish src/Deckle.Installer/Deckle.Installer.csproj -c Release -r win-x64
```

Output: a single `Deckle-Installer.exe` under `…\win-x64\publish\`.

## Technical decisions

- **NativeAOT.** A small native exe, instant cold start, no .NET runtime to unpack —
  the right shape for a stub the user double-clicks once. Forces AOT-safe code
  (source-generated JSON and COM, `LibraryImport`).
- **Zero third-party packages.** Registry, COM source-gen interop, `System.Text.Json`
  and HTTP all ship in `net10.0-windows` — conforms to the project's no-dependency
  doctrine. The `Downloader` is a console-local re-take of
  `Deckle.Transcription.Setup.Downloader` rather than a reference, precisely to avoid
  dragging WinUI into the stub.
- **TFM inherited** (`net10.0-windows10.0.26100.0`). A bare `net10.0-windows` clashes
  with the repo's `TargetPlatformMinVersion` (NETSDK1135); the AOT linker trims the
  unused WinRT projections, so the image stays lean without the deviation.
- **Release resolution via the REST API, not `/releases/latest`.** Every `0.x` release
  is a pre-release, which `latest` skips; the `/releases` list returns the true newest.
  Asset URLs follow the frozen convention
  (`releases/download/v<X.Y.Z>/Deckle-v<X.Y.Z>.zip` + `.sha256`), read from the assets
  list when present, convention as fallback.
- **Start Menu shortcut via source-generated COM** (`IShellLink`/`IPersistFile` +
  `CoCreateInstance` + `StrategyBasedComWrappers`), because classic ComImport coclass
  activation isn't AOT-supported. No Desktop shortcut (modern-Windows stance).
- **Per-user, no admin.** Install folder, shortcut and Installed-apps entry all under
  the user profile / HKCU.

## Not in V1

No code-signing (SmartScreen "More info → Run anyway", documented in the release
notes). No in-installer auto-update — a future in-app updater will consume the same
release convention. No dev-vs-user branching: the app is self-contained, the dev path
(clone + F5) is documented separately.
