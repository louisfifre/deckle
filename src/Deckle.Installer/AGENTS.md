---
description: NativeAOT console stub that downloads, installs, and uninstalls Deckle per-user, no admin.
type: agent-instructions
---

# AGENTS.md — Deckle.Installer

The download stub that brings Deckle onto an end user's PC: a standalone NativeAOT console exe, shipped on its own, not referenced by any other module. The same exe is both installer and uninstaller (`--uninstall`, the registered UninstallString, re-runs it in reverse). Per-user, no admin — install folder, shortcut and Installed-apps entry all under the user profile / HKCU.

Its contract is narrow: download the app payload → verify SHA-256 → extract → integrate (Start Menu, Installed-apps, `DECKLE_DATA_ROOT`) → launch. Re-run over an existing install (recognised from the Installed-apps registration), the same contract reads as an update: folders pre-filled from the live install, binaries replaced, data untouched. It never touches natives, models, or Ollama — runtime provisioning is the app's own first-run job (`Deckle.Setup`). Two folders, the whole UX point: binaries (per-user) and data/models (relocatable off a saturated C: via `DECKLE_DATA_ROOT`).

Non-obvious decisions:

- **Release resolution via the `/releases` REST list, not `/releases/latest`.** Every `0.x` release is a pre-release, which `latest` skips; the list returns the true newest.
- **The exe copies itself into the install folder as the uninstaller** — nothing separate to build or stage; the same binary with `--uninstall` reverses the install.
- **`DECKLE_DATA_ROOT` is written to `HKCU\Environment` only on a non-default data folder.** On the default, the app's own hardcoded default stands — no trace to clean up on uninstall.
