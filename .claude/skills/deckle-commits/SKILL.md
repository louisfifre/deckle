---
name: deckle-commits
description: Commit doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries the adapted Conventional Commits format, the closed vocabulary of types and module-aligned scopes, the grain doctrine that imposes one semantic intent per commit, the handling of cross-module overhauls, the feature-branch merge commit convention, and the author identity rule that excludes any LLM co-signature trailer. Invoke before every commit act, before defining a sequencing strategy for a large workstream, and during a history audit. Triggers on phrases like deckle commit, deckle commit message, deckle conventional commits, deckle commit scope, deckle commit grain, split deckle commit, deckle megacommit, deckle cross-cutting refactor commit, deckle merge commit, deckle commit author identity, deckle history audit, Co-Authored-By Claude deckle.
---

# Deckle — Commit doctrine

## Role

Project-specific skill that answers a recurring question: **which commit, with which message, at what grain**. Invoked before every non-trivial `git commit`, when sequencing a large workstream into intermediate commits, and when auditing the history of a branch or of the repo.

Complements two distinct resources. `git-commit` (global skill) carries the **mechanics** of execution — analyzing the diff, staging, phrasing, executing. `personal-conventions` carries the **cross-project rules** — language, branch conventions, worktrees. `deckle-commits` is the project-local layer that encodes the doctrine applied by the engine and the Deckle-specific choices (scope vocabulary, expected granularity, author identity).

## Semantic posture

A commit represents **one clear and self-contained intent**. Neither "everything done in the day", nor "everything that touches a module". Three properties follow that are the benefits sought and the test of the doctrine. **Bisectability** — `git bisect` must be able to isolate the cause of a bug to a precise commit; a commit that mixes two changes destroys this property. **Historical readability** — sequential reading of `git log` tells an intelligible progression; a megacommit collapses that narrative into mush. **Targeted reversibility** — a `git revert` must be able to undo a step without breaking the rest; two fused intents force an all-or-nothing revert.

The inverse rule also applies: a commit that does only half of a change, leaving the repo in an inconsistent state, is not atomic either. Atomicity is the minimal semantic unit that leaves the code in a state that compiles and holds together.

## Adopted format

Conventional Commits v1.0.0 (see [conventionalcommits.org](https://www.conventionalcommits.org/en/v1.0.0/)). The canonical form is `type(scope): description`, subject in imperative present, first letter lowercase, no trailing period. Target length **72 characters for the subject**, which is the readable length in `git log --oneline` and the GitHub UIs without truncation; the strict 50/72 rule inherited from Tim Pope is an ideal — Deckle relaxes it to 72 for the subject because the `type(scope):` combination already consumes characters and the readability of the raw subject takes precedence over the conciseness ideal.

The optional body is separated from the subject by a **blank line**, wrapped at 72 characters, phrased to say **why** the change exists — not what the diff already shows. Footers live after a final blank line and carry traceable references: `refs ADR-NNNN` when the commit enacts a decision documented in an ADR, `refs #123` for a ticket, `BREAKING CHANGE: …` for a change of external contract (Deckle does not yet have a publicly consumed release, so this mention mostly serves to flag what will need to surface at the time of a 1.0).

## Closed vocabulary of types

Eleven admitted types, aligned with the standard. **`feat`** introduces a new feature or user-facing behavior. **`fix`** fixes a bug. **`refactor`** changes internal structure without modifying observable behavior. **`docs`** exclusively touches documentation. **`test`** adds or modifies tests. **`perf`** improves a measurable performance. **`style`** corrects formatting without logic. **`build`** modifies the build system, dependencies, packaging scripts. **`ci`** modifies continuous integration (Deckle does not have one yet, but the type stays reserved). **`chore`** is the receptacle for maintenance that does not fit elsewhere (`.gitignore`, config files, housekeeping). **`revert`** undoes a prior commit.

One local type kept: **`merge`** for the merge commits of feature branches into `main`, in the form `merge: <branch-name> — <short summary>`. Preserves the readability of merges flat in `git log --oneline`. It is an assumed deviation from the Conventional Commits spec, justified by the project workflow (`--no-ff` on feature branches).

**Types to proscribe** because they emerged ad hoc and fragment the vocabulary: `prep`, `tune`, `tools`, `bench`, `tweak`, `hud`, `settings`, `engine`, `logs`. These intents all fall into `feat`, `refactor`, `chore` or `docs`. For benchmark iterations, the right format is `chore(bench): iteration N — …` — the scope carries the context, not the type.

## Closed vocabulary of scopes

The scope reflects the **boundary touched**, not the author or the environment. For Deckle, it mirrors the list of canonical modules — `core`, `audio`, `vision`, `lighting`, `ambient`, `chrono`, `composition`, `catalog`, `shell`, `settings`, `whisp`, `llm`, `playground`, `hud` (in the sense of `Deckle.Chrono.Hud`). Three cross-cutting scopes are admitted when the commit does not touch a particular module but a boundary of the project: **`scripts`** for `scripts/`, **`docs`** for `docs/` at the root (and only appears redundantly with the type `docs:` when one wants to disambiguate a precise page), **`agent`** for the `CLAUDE.md` files and the skills under `.claude/`.

**One single scope per commit.** The comma-separated form `feat(playground, ambient): …` that appeared in history is a splitting signal: either the commit blends two intents and must be split, or the actual scope is a cross-module theme (`refactor(observability)`, `refactor(catalog)`) that must be named. If a cross-cutting thematic scope starts appearing repeatedly, it is a signal to promote it to a dedicated sub-namespace — see `deckle-modularite`.

## Grain doctrine — when to split

A commit must be summarizable by **one sentence without `and` or `+`**. The presence of a `+` in the subject is the most reliable signal of a disguised megacommit: `chore: gitignore cleanup + untrack docs/archives` is two commits, `refactor(playground): States/Primitive sections + native Play/Pause toggle` is two commits. Each intent must be able to live and be reverted alone.

Canonical cases per workstream typology. **Cross-cutting overhaul such as the EventSource migration** — one infrastructure commit (interfaces, base class, boot registration), then one commit per migrated module (clear intent: migrate this module), then one commit switching the legacy sinks, then one commit cleaning up the stubs. No final megacommit that piles everything up. **Bug fix** — one commit for the fix, possibly one commit for the tests if coverage is added jointly. If the fix exposes a prerequisite refactor, the refactor is a separate commit upstream. **UI overhaul** — one commit per refactored surface, never an end-of-day dump. The UX copy pass on a page and the structural overhaul of the same page are two commits. **Renaming a module or an exposed symbol** — one commit for the rename alone (`refactor(catalog): rename Localization → Catalog`), then the functional content; this discipline makes the rename visible and spares it from a revert that would cancel real work.

## Grain doctrine — when to merge

The counterpart exists: a change is not atomic because it is small, it is atomic because it **forms a self-contained testable unit**. Three legitimate fusion cases. **Signature and callers** — modifying the signature of a public method and propagating the calls in the same commit, because an intermediate commit would not compile. **Resource and consumption** — adding a `.resw` key and consuming it in the matching XAML, because the orphan key has no meaning in isolation. **File rename and references** — moving a file and updating its `using` directives, because the repo does not hold together between the two.

A modification of foreign scope that slipped into a commit in progress **does not fuse opportunistically**. You undo with `git restore --staged` or `git reset`, you commit the main intent, then you commit the incidental modification separately.

## Cross-cutting refactor case

When a workstream touches several modules, two strategies. The **canonical** route is splitting by cross-module semantic step — one commit per migrated module, with the scope of the touched module. This route preserves bisectability and tells the progression of the workstream. The **thematic** route, rarer, is worth it when the operation is semantically indivisible — for example an atomic rename of a public symbol consumed everywhere. The scope is then the cross-module theme (`refactor(catalog)`, `refactor(observability)`), and the commit stays unique because splitting it would produce non-compiling intermediate states.

The choice criterion: **do intermediate commits leave the repo in a state that compiles and holds together**? If yes, split by module. If no, single thematic scope. The anti-megacommit rule remains: this unique commit stays the minimal semantic unit of the change, not a dump.

## Merge commits

Project strategy: feature branches merged into `main` with `--no-ff`, never squash rebase. The merge commit receives as message `merge: <branch-name> — <short summary of the branch intent>`. The short summary is the cover sentence readable in `git log --oneline`; the internal commits of the branch stay visible via `git log <branch>` and are the raw material of bisectability.

The quality of a merge commit is **derived from the internal discipline of the branch**. If the internal commits are themselves compound or ambiguous dumps, no merge summary remedies that. The doctrinal responsibility is upstream, in each individual commit of the feature branch.

## Author identity

All commits go out under the identity of the maintainer (`Louis <git@louisfifre.com>`). **Never** a `Co-Authored-By: Claude <…@anthropic.com>` trailer, **never** a `🤖 Generated with [Claude Code](…)` line. These markers register Claude as a visible GitHub contributor, which is factually false: an LLM agent is not a contributor in the version-control sense. The rule is carried by the project's root `CLAUDE.md`; it is restated here because it is precisely the act of committing that puts it at stake, and that is the moment when the temptation to inscribe the agent reappears.

## Three audit signals before sending

Before executing `git commit`, three review questions that catch the majority of drifts observed in the Deckle history. **Does the subject contain a `+` or an `and`** that joins two distinct intents? Split. **Does the subject exceed 72 characters without any intent being removable**? It is probably two commits camouflaged as one. **Is the scope comma-separated** or imprecise (`(playground, ambient)`, `(misc)`)? Pick the main scope and split the other intent, or name a legitimate thematic scope.

## Pointers

- **`git-commit`** (global skill) — execution mechanics, diff analysis, generic Conventional Commits format. `deckle-commits` specifies the doctrine for Deckle.
- **`personal-conventions`** — cross-project rules (language, branch conventions, worktrees). `deckle-commits` applies them to the project.
- **`deckle-docs`** — documentation convention and ADR. When a commit enacts a tracked decision, the body mentions `refs ADR-NNNN`.
- **`deckle-modularite`** — module boundaries. Commit scopes mirror this list; a scope that does not figure there is a signal either of an invented scope, or of a missing module to promote.
- **`deckle-nomenclature`** — naming vocabulary, including the module names that serve as scopes.
- **`deckle-refonte`** — orchestrator skill. A multi-strand overhaul sequenced via intermediate commits invokes this skill for the splitting strategy.
- **[conventionalcommits.org](https://www.conventionalcommits.org/en/v1.0.0/)** — normative reference spec.
