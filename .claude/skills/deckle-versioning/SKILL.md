---
name: deckle-versioning
description: Versioning and changelog doctrine for Deckle: how versions are numbered (SemVer read as compatibility, the app-level reading, the 0.x mapping), how the changelog is written (keepachangelog, a closed category vocabulary, curate-not-generate), and how release notes are derived. Invoke before cutting a version, writing a CHANGELOG entry, or drafting release notes. Triggers like deckle version, version bump, semver, changelog, keepachangelog, release notes, how to version, what version is this, deprecate a feature.
type: skill
---

# Deckle — Versioning and changelog doctrine

## Role

How Deckle numbers its versions and records what changed between them. Answers three recurring questions: **which number to bump**, **what goes in the changelog**, and **what a release note is**. Complements `deckle-workflow`, which owns the *operational* side of a release — the version-bound push, `main` hygiene, the bump/tag/push mechanics; this skill owns the *semantics* of the numbers and the prose. Sits on top of `deckle-commits`: the Conventional Commits history is the raw material a changelog is distilled from.

## SemVer — the version measures compatibility

The reference is [SemVer 2.0](https://semver.org/). The number is `MAJOR.MINOR.PATCH` and it measures **compatibility**, not the size of a change: MAJOR breaks, MINOR adds without breaking, PATCH fixes without breaking. That boundary is a contract aimed at **consumers of a public API** — typically a package manager that must decide whether an upgrade is safe.

Deckle has **no public API and no downstream consumer** (local app, no installer yet, nothing resolves it as a dependency). The API-compatibility criterion is therefore hollow; compatibility is read at the **user/behaviour** level instead — does the user's habit still hold. The `0.x` phase formally means "no stability promised, anything may change", which is why Deckle can legitimately stay in `0.x` for a long time.

Within that latitude, the Deckle mapping (a project convention, not the spec):

- **MAJOR** — an overhaul: the UI, the backend, or the paradigm changes.
- **MINOR** — a significant cycle: a feature, or an engine/architecture change (e.g. `0.4.0` for the streaming socle).
- **PATCH** — a fix or a small step.

No `1.0` before there is an installer and a stable behaviour surface users depend on. The `<Version>` in `Deckle.App.csproj` is the single source.

## Changelog — keepachangelog

The companion convention is [keepachangelog 1.1.0](https://keepachangelog.com/) — its principle #7 is literally "declare adherence to SemVer". The two are a pair: SemVer is the index, the changelog is the content.

A version cut adds a curated entry to `CHANGELOG.md` (root), authored with the `chore(release)` commit: **one entry per version, newest first, ISO date `YYYY-MM-DD`**. The entry is **curated for the reader, never a raw `git log` dump** — that is the explicit keepachangelog anti-pattern.

Closed vocabulary — **six categories, in this order**, and only these six:

- **Added** — a new capability.
- **Changed** — a behaviour change to an existing capability.
- **Deprecated** — still present, slated for removal.
- **Removed** — gone.
- **Fixed** — a bug correction.
- **Security** — a vulnerability patch.

Any extension is **non-canonical and must be marked as such**: e.g. a `Known issues` block for an experimental opt-in shipped to testers goes *after* the six categories, explicitly labelled a Deckle extension. The default is to fold the caveat into the entries rather than extend.

Deprecation discipline (SemVer FAQ): a capability is announced under **Deprecated** in a MINOR, kept for at least one cycle, then **Removed** in a MAJOR — never removed cold. This is the path for, e.g., retiring the monolithic transcription engine once streaming is the default.

## Tooling — no new dependency, the agent curates from `git log`

Because Deckle already writes Conventional Commits (`deckle-commits`), the changelog is distilled from the history with **plain git — no added tool**. At a version cut the agent reads `git log <previous-tag>..HEAD`, sorts the commits into the six categories, and **curates**: it decides the hierarchy — what is a headline change versus a minor fix — which is exactly the judgement keepachangelog asks for and a mechanical generator cannot make.

`git-cliff` (a single Rust binary that does this from Conventional Commits) and `release-please` (a GitHub Actions / Release-PR flow) were both considered and **not adopted**: Louis is reticent about dependencies, the agent already writes the commits and can curate directly, and there is no CI. We borrow only git-cliff's *idea* (Conventional Commits → a categorized entry), not the binary. Even a one-time historical backfill of `CHANGELOG.md` (0.2.0 → today) is done by hand from `git log`.

## Release notes

Distinct from the changelog and **derived** from it. The changelog is dev-facing, factual, in-repo; release notes are **user-facing**: narrative, benefit-framed, with visuals, living in the **GitHub Release body** (and eventually an in-app surface — a Playground hub or a tray entry, deferred until there is a home).

Deckle policy: release notes are written **only for MINOR (`Y`) cuts**, not for patches. A GitHub Release in the `0.x` phase is marked **pre-release**. The full release-notes doctrine (tone, structure, visuals) is otherwise deferred.

## Pointers

- **`deckle-workflow`** — the operational release: version-bound push, `main` hygiene, the bump/tag/push mechanics.
- **`deckle-commits`** — Conventional Commits, the raw material a changelog distils from.
- **[semver.org](https://semver.org/)**, **[keepachangelog.com](https://keepachangelog.com/)** — normative references.
