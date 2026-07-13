---
description: Windows integration of an installed Deckle — install locations, Start Menu shortcut, Installed-apps registration, DECKLE_DATA_ROOT, running-process gate.
type: agent-instructions
---

# AGENTS.md — Deckle.Install

The Windows-integration primitives shared by the download stub (`Deckle.Installer`, NativeAOT) and the install-mode wizard (`Deckle.Setup`): default install locations (`InstallPaths`), the Start Menu shortcut (`Shortcut`), the Installed-apps registration (`UninstallEntry`), the `DECKLE_DATA_ROOT` user variable (`UserEnvironment`), and the running-process gate before touching binaries (`RunningProcesses`). Everything here is per-user, no admin — HKCU and the user profile only.

The constraint that shapes the module: it sits below both consumers, so it must satisfy the stricter one. The stub is NativeAOT with zero packages — this module is therefore **dependency-free and AOT-safe**: no WinUI, no Deckle.* reference, Win32 + registry + source-generated COM (`GeneratedComInterface` for `IShellLinkW`/`IPersistFile`) only. Adding a package or a Deckle reference here breaks the stub's contract before it breaks the build.
