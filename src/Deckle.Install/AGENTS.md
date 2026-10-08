---
description: Windows integration primitives shared by the stub and the wizard.
type: agent-instructions
---

# AGENTS.md — Deckle.Install

- **Dependency-free and AOT-safe.** This module sits below the NativeAOT stub (`Deckle.Installer`) and the install-mode wizard (`Deckle.Setup`), so it meets the stricter contract: no package, no WinUI, no Deckle.* reference. Use Win32, the registry, source-generated COM and source-generated JSON only. A package or a Deckle reference breaks the stub before it breaks the build.
- **Per-user only.** HKCU and the user profile, no admin.
- **`ReleaseResolver` lives here** so its two consumers, the stub's download and the app's update check, share one implementation of the release convention. It uses source-generated JSON.
