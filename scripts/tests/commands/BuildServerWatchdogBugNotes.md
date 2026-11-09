# Build-server watchdog bug notes

## The build-server watchdog kept a merged worktree's folder open

- Trigger: removing a worktree with `git worktree remove` after a build in it,
  while other sessions keep building.
- Symptom: « Permission denied »; git unregistered the worktree but its folder
  stayed, with one empty directory, `src\Deckle.Core`, impossible to delete.
- Cause: `scripts/build-server-watchdog.ps1` is armed by MSBuild's Exec, which
  runs in the folder of the project being built, and the watchdog process
  inherited it as its working directory. A process holds its working directory
  open, and the watchdog lives until no build anywhere has run for five
  minutes. Established by reading the current directory of every process: the
  only holder was that watchdog.
- Violated invariant: a long-lived process started from a build never keeps a
  checkout's folder as its working directory. The watchdog now starts in its
  state directory (comment at the `Start-Process` call in
  `scripts/build-server-watchdog.ps1`).
- Recurrence cue: a worktree folder that cannot be deleted, often an empty
  project directory, while a pwsh or other helper process launched by a build
  is still running.
- Guarded by: no automated test; `scripts/tests/commands/build-server-watchdog.tests.ps1`
  covers the idle rule, not the working directory. Checked by hand on
  2026-11-09: a watchdog armed from another folder reported its state directory
  as working directory.
