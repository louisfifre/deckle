---
name: deckle-versioning
description: Use when you record a Deckle version, touch CHANGELOG.md or prepare release notes, to choose the bump and let the commit history write the text.
---

## Why

The commit history is the only source of the changelog and the release notes, generated offline with plain git. The record stays as local and autonomous as the app, and the two texts can never disagree.

## Statement

The version lives in one place, the `<Version>` of `src/Deckle.App/Deckle.App.csproj`. A version record is one `chore(version): vX.Y.Z` commit, made by `scripts/commands/record-version.ps1`, and the global instructions say when it happens, at each merge into main. A git tag marks a public release only, created after the release artifacts build successfully. The native runtime bundle has its own version and its own `native-vX.Y.Z` tags, released apart from the app by `scripts/commands/release/publish-native-runtime.ps1`.

Magnitude follows what the user sees, not API compatibility. MAJOR is an overhaul. MINOR is a real cycle, a feature or an engine change. PATCH is everything else, a fix, an internal change, a documentation workstream or another small step. Deckle stays in 0.x until its behaviour surface is stable enough to promise. A 0.x release is still a GitHub release and claims the repository's "Latest" badge, which native runtime releases always refuse. The GitHub pre-release flag is reserved for a build sent out for testing under a suffixed tag such as `v0.58.0-beta.1`; `publish-app.ps1` does not cut one yet.

The changelog follows Keep a Changelog. Its six categories keep their standard order, and the newest version comes first, with an ISO date. `scripts/commands/changelog.ps1` regenerates the whole file from the Conventional Commits history, from the `v0.4.0` floor forward, with no external tool and no network. Earlier history is summarised, not itemised. `release-history.json` is the offline ledger of public release tags, and historical internal tags are ignored. Between releases, each version record refreshes one `[Unreleased]` accumulator from the latest public release to `HEAD`. A public release appends its tag to the ledger and freezes the accumulator into the new version section. Only feat, fix, perf, refactor, deprecate and revert reach the changelog, under Added, Fixed, Changed, Changed, Deprecated and Removed. A subject with the `!` marker lands under Removed whatever its type. A revert whose body carries git's `This reverts commit <sha>` line cancels itself and its target when both sit in one range, and without the pointer it stays a Removed. Any other subject is dropped, so a change surfaces only when its commit is written conventionally. A capability is announced Deprecated in a MINOR by a `deprecate` commit and only Removed in a MAJOR by a `!` commit, never cold.

Release notes are the same accumulator's output. At publish time `publish-app.ps1` asks `changelog.ps1` for the notes of the version and feeds them to the GitHub release. The `-Notes` parameter is the maintainer's override for a release whose history cannot speak for itself. A push has no changelog or release meaning by itself.
