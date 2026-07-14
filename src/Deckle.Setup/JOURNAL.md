---
description: Dated decisions and findings for the setup wizard — installer, updater, relocation.
type: module-journal
---

# Journal — Deckle.Setup

Module-level dated notes. Most recent on top.

## 2026-07-14 — In-app updater and data-root relocation landed

- **Updater bootstrap constraint.** The update chain hands off to the NEW payload's `Deckle.exe --update-apply`; a payload predating that verb ignores it and boots the app normally from the temp extraction. The first real update therefore requires both sides to carry this code: an installed release with the updater, and a newer published release whose payload understands `--update-apply`. Until such a pair exists, "Install now" against an older release mis-launches instead of updating — do not exercise it on a real install.
- **The wizard never persisted the chosen model** (fixed): the choice only landed on disk as a downloaded file, `Engine.Model` kept its initializer, and a swap to an already-installed model was a no-op because the install plan skips present items. The choice is now written at the end of the install run, in the process whose AppPaths owns the chosen data root.
- **Update check is gated on the installed launch** — `UninstallEntry` install dir + running image path must match. A dev build shares the HKCU hive; without the gate it would both offer updates and apply them over the worktree.
