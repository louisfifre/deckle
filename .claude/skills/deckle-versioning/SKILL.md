---
name: deckle-versioning
description: How versions are numbered and the changelog written. Invoke before cutting a version, writing a CHANGELOG entry, or drafting release notes.
type: skill
---

# Deckle — Versioning

## Intent

Decide which number to bump and what goes in the changelog.

## How

Versions track user-facing change, not API compatibility. MAJOR is an overhaul, MINOR a real cycle (a feature, an engine change), PATCH a fix or small step. Stay in 0.x until the behaviour surface is stable enough to promise.

The changelog follows keepachangelog — six categories in order (Added, Changed, Deprecated, Removed, Fixed, Security), newest first, ISO date. It is generated from the Conventional-Commit history by `scripts/lib/changelog.ps1` (the `deckle.ps1` Release menu), with no external tool and no network: plain `git log` keeps the changelog as local and autonomous as the app itself. Types route to categories — feat→Added, fix→Fixed, perf/refactor→Changed, revert→Removed; housekeeping (chore/docs/test/ci/build/style), merges and non-conventional subjects are dropped, so a change surfaces only if its commit is written conventionally. The file is regenerated wholesale from the `v0.4.0` floor forward and is not hand-edited; earlier history predates the discipline and is summarised, not itemised. A capability is announced Deprecated in a MINOR and only Removed in a MAJOR, never cold.

Release notes are the same generator's output: at publish time `publish-app.ps1` calls `changelog.ps1 -NotesFor <version>` and feeds the result to `gh release create --notes-file`. No hand authoring, no GitHub auto-notes.
