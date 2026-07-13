---
description: NativeAOT download stub — silent, no console; fetches the payload to temp and hands off to the WinUI first-run wizard. Also the registered uninstaller.
type: agent-instructions
---

# AGENTS.md — Deckle.Installer

The download stub that brings Deckle onto an end user's PC: a standalone NativeAOT `WinExe`, shipped on its own, not referenced by any other module. It is the web-installer half of a fused setup (VS Code / Discord pattern) — the other half is the app's own WinUI first-run wizard, which runs from the freshly downloaded payload. Per-user, no admin.

Its install contract is now narrow and transparent: resolve the latest GitHub release → download the app payload zip into a unique temp folder → verify SHA-256 → extract it there → launch `Deckle.exe --install --stub "<this exe>" --cleanup "<temp root>"` from the extracted folder → exit. Everything else — folder choice, module selection, the Start Menu shortcut, the Installed-apps registration, `DECKLE_DATA_ROOT`, the binary copy — moved into the wizard. The stub never touches the install folder during install, and never touches natives, models, or Ollama.

The same exe is still the uninstaller. The wizard copies it into the install folder and registers `--uninstall` as the UninstallString; re-run there, it reverses the integration (deregister shortcut and Installed-apps key, optionally drop the data folder, schedule the binaries — itself included — for deletion). `-y`/`--yes` is the quiet path the Installed-apps QuietUninstallString drives.

There is no console. Install shows a native Win32 progress window (marquee while resolving and extracting, determinate with real byte counts while downloading); uninstall shows two confirmation message boxes then a marquee window while removing. Failures surface as a message box, never a log.

Non-obvious decisions:

- **`WinExe`, not `Exe`.** A console stub would flash a window; the fused setup must be as transparent as a browser's web installer. Every `Console.*` is gone — inert under WinExe anyway.
- **The message loop runs on the main thread; the work runs on a background Task.** A Win32 window must be serviced on its creating thread. The worker never touches an HWND — it stashes state under a lock and PostMessages `WM_APP_UPDATE`; the WndProc applies it on the UI thread. `WM_APP_DONE` tears the window down on completion; the title-bar X arrives as `WM_CLOSE` and cancels the token.
- **The WndProc is an `[UnmanagedCallersOnly]` static reached by function pointer.** A runtime-marshalled delegate is not AOT-safe; the function pointer is. One window per run, so it routes through a single static instance rather than a GWLP_USERDATA GCHandle.
- **The temp folder is left in place on success.** The wizard reads from the extracted tree and owns cleanup — hence `--cleanup "<temp root>"`. The downloaded zip is deleted right after extraction (dead weight). On any failure the whole temp folder is removed best-effort.
- **Release resolution via the `/releases` REST list, not `/releases/latest`.** Every `0.x` release is a pre-release, which `latest` skips; the list returns the true newest.
- **The uninstaller schedules its own folder's deletion via a detached `cmd`, as the last step.** A process can't delete its own image; the `ping`-delayed `rmdir` waits for exit. Scheduled only after the window is gone, so nothing races the delete. The install path is validated against cmd metacharacters first.
