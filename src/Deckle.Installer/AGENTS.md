---
description: NativeAOT download stub and registered uninstaller.
type: agent-instructions
---

# AGENTS.md — Deckle.Installer

- A standalone NativeAOT `WinExe`, shipped on its own and referenced by no module. Per-user, no admin.
- The install contract is narrow: resolve the release, download to a temp folder, verify SHA-256, extract, launch `Deckle.exe --install`, exit. The wizard does everything else. The stub never touches the install folder during install, nor natives or models.
- The same exe is the uninstaller. `-y`/`--yes` is the quiet path.
- Deckle.Install stays dependency-free and AOT-safe for this stub; see `Deckle.Install/AGENTS.md`.

## Decisions

- **`WinExe`, not `Exe`.** A console stub would flash a window; the stub must be as transparent as a browser's web installer.
- **The message loop runs on the main thread, the work on a background task.** A Win32 window must be serviced on its creating thread. The worker never touches an HWND: it stores state under a lock and posts a message.
- **The WndProc is an `[UnmanagedCallersOnly]` static reached by function pointer.** A runtime-marshalled delegate is not AOT-safe.
- **The temp folder stays on success.** The wizard reads the extracted tree and owns cleanup. On failure the stub removes it best-effort.
- **Releases resolve through the `/releases` list, not `/releases/latest`.** Every `0.x` release is a pre-release, which `latest` skips.
- **The uninstaller deletes its own folder through a detached `cmd`, as the last step.** A process cannot delete its own image. Validate the install path against cmd metacharacters first.
