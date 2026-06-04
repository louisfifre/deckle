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

The changelog follows keepachangelog: one curated entry per version, newest first, ISO date, never a raw git-log dump. Six categories, in order — Added, Changed, Deprecated, Removed, Fixed, Security. It is distilled by hand from the commit history; no tool is added for it. A capability is announced Deprecated in a MINOR and only Removed in a MAJOR, never cold.

Release notes aren't authored by hand — GitHub generates them; their doctrine comes later.
