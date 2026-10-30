---
name: deckle-versioning
description: Use when you record a Deckle version, touch CHANGELOG.md or prepare release notes, to choose the bump and let the commit history write the text.
metadata:
  version: "2.0"
---

## Why

The commit history is the only source of the changelog and the release notes, generated offline with plain git. The record stays as local and autonomous as the app, and the two texts can never disagree.

## Statement

The version is a git tag. An annotated tag `vX.Y.Z` sits on the commit that enters main at each merge, created by `scripts/git/record-version.ps1 -Bump <patch|minor|major> -Apply`, which previews without `-Apply` and pushes the tag only with `-Push`; no file carries the number. A build reads the highest version tag reachable from its HEAD, through the `ReadVersionFromGit` target of `src/Deckle.App/Deckle.App.csproj`, and refuses to build without one, so a fresh clone fetches its tags first. A commit after the tag still belongs to that version until the next tag is recorded. The native runtime bundle keeps its own version and its own `native-vX.Y.Z` tags, released apart from the app by `scripts/release/publish-native-runtime.ps1`.

A public release is marked by a second annotated tag, `release/vX.Y.Z`, on the released commit. `scripts/release/publish-app.ps1 -Publish` creates it once the GitHub release is public, then regenerates and commits `CHANGELOG.md`; `scripts/git/mark-release.ps1` creates the same mark by hand. The GitHub release itself is attached to the `vX.Y.Z` tag, which `publish-app.ps1` pushes with it.

Magnitude follows what the user sees, not API compatibility. MAJOR is an overhaul. MINOR is a real cycle, a feature or an engine change. PATCH is everything else, a fix, an internal change, a documentation workstream or another small step. Deckle stays in 0.x until its behaviour surface is stable enough to promise. A 0.x release is still a GitHub release and claims the repository's "Latest" badge, which native runtime releases always refuse. The GitHub pre-release flag is reserved for a build sent out for testing under a suffixed tag such as `v0.58.0-beta.1`; `publish-app.ps1` does not cut one yet.

The changelog follows Keep a Changelog. Its six categories keep their standard order, and the newest version comes first, with an ISO date. `scripts/git/changelog.ps1` regenerates the whole file from the Conventional Commits history, with no external tool and no network: it previews by default and writes with `-Apply`. The release marks are its boundaries, one section per mark with the commits since the previous one, and `[Unreleased]` holds what came since the latest mark; the first mark, `release/v0.4.0`, gathers everything before it. A version section therefore appears only once its release is public. Only feat, fix, perf, refactor, deprecate and revert reach the changelog, under Added, Fixed, Changed, Changed, Deprecated and Removed. A subject with the `!` marker lands under Removed whatever its type. A revert whose body carries git's `This reverts commit <sha>` line cancels itself and its target when both sit in one range, and without the pointer it stays a Removed. Any other subject is dropped, so a change surfaces only when its commit is written conventionally. A capability is announced Deprecated in a MINOR by a `deprecate` commit and only Removed in a MAJOR by a `!` commit, never cold.

Release notes are the same generator's output. At publish time `publish-app.ps1` asks `changelog.ps1 -NotesFor <version>` for the section of the version and feeds it to the GitHub release. The `-Notes` parameter is the maintainer's override for a release whose history cannot speak for itself. A push has no changelog or release meaning by itself.
