---
name: deckle-versioning
description: How versions are numbered and the changelog written. Invoke before cutting a version, writing a CHANGELOG entry, or drafting release notes.
---

# Deckle — Versioning

## Intent

Decide which number to bump and what goes in the changelog.

## How

A version record is a `chore(version): vX.Y.Z` commit, not a tag; when it happens is the global convention's call. Git tags are public-release boundaries only and are created after the release artifacts build successfully. Magnitude follows the user-facing change rather than API compatibility: MAJOR is an overhaul, MINOR a real cycle (a feature or engine change), and PATCH is a fix, internal change, documentation workstream, or other small step. Stay in 0.x until the behaviour surface is stable enough to promise.

The changelog follows keepachangelog — six categories in order (Added, Changed, Deprecated, Removed, Fixed, Security), newest first, ISO date. It is generated from the Conventional-Commit history by `scripts/commands/changelog.ps1` (the `deckle.ps1` Release menu), with no external tool and no network: plain `git log` keeps the changelog as local and autonomous as the app itself. `release-history.json` is the offline source of truth for public release tags; historical internal tags are deliberately ignored. Between releases, every version record refreshes one `[Unreleased]` accumulator from the latest public release to `HEAD`. A successful public release appends its tag to the ledger and freezes that accumulator into the new version section. Types route to categories — feat→Added, fix→Fixed, perf/refactor→Changed, deprecate→Deprecated, revert→Removed; housekeeping (chore/docs/test/ci/build/style), merges and non-conventional subjects are dropped, so a change surfaces only if its commit is written conventionally. A `!` subject lands under Removed whatever its type. A commit whose body carries git's `This reverts commit <sha>` line cancels itself and its target when both sit in one range; without the pointer a revert stays a Removed. The file is regenerated wholesale from the `v0.4.0` floor forward and is not hand-edited; earlier history predates the discipline and is summarised, not itemised. A capability is announced Deprecated in a MINOR by a `deprecate` commit and only Removed in a MAJOR by a `!` commit, never cold.

Release notes are the same accumulator's output: at publish time `publish-app.ps1` calls `changelog.ps1 -NotesFor <version>` and feeds the result to `gh release create --notes-file`. No hand authoring, no GitHub auto-notes. Pushes have no changelog or release semantics by themselves.
