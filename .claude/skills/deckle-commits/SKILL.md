---
name: deckle-commits
description: Commit doctrine for Deckle: the adapted Conventional Commits format, one-intent-per-commit grain, module-derived scopes, the merge-commit convention, and the no-LLM-co-author rule. Invoke before a commit, when sequencing a workstream into commits, or during a history audit. Triggers like deckle commit, commit message, commit grain, split this commit, merge commit, commit scope, Co-Authored-By Claude.
type: skill
---

# Deckle — Commit doctrine

## Role

Project-specific skill that answers a recurring question: **which commit, with which message, at what grain**. Invoked before every non-trivial `git commit`, when sequencing a large workstream into intermediate commits, and when auditing the history of a branch or of the repo.

Complements `personal-conventions`, which carries the **cross-project rules** — language, branch conventions, worktrees — that this skill applies to the project. `deckle-commits` is the full commit doctrine for Deckle: the format, the closed vocabularies of types and scopes, the grain doctrine, and the project-specific choices (expected granularity, author identity). The generic mechanics of executing a commit — analyzing the diff, staging, running `git commit` — are baseline git and are not restated here.

## Semantic posture

A commit represents **one clear and self-contained intent**. Neither "everything done in the day", nor "everything that touches a module". Three properties follow that are the benefits sought and the test of the doctrine. **Bisectability** — `git bisect` must be able to isolate the cause of a bug to a precise commit; a commit that mixes two changes destroys this property. **Historical readability** — sequential reading of `git log` tells an intelligible progression; a megacommit collapses that narrative into mush. **Targeted reversibility** — a `git revert` must be able to undo a step without breaking the rest; two fused intents force an all-or-nothing revert.

The inverse rule also applies: a commit that does only half of a change, leaving the repo in an inconsistent state, is not atomic either. Atomicity is the minimal semantic unit that leaves the code in a state that compiles and holds together.

## Adopted format

Conventional Commits v1.0.0 (see [conventionalcommits.org](https://www.conventionalcommits.org/en/v1.0.0/)). The canonical form is `type(scope): description`, subject in imperative present, first letter lowercase, no trailing period. Target length **72 characters for the subject**, which is the readable length in `git log --oneline` and the GitHub UIs without truncation; the strict 50/72 rule inherited from Tim Pope is an ideal — Deckle relaxes it to 72 for the subject because the `type(scope):` combination already consumes characters and the readability of the raw subject takes precedence over the conciseness ideal.

The optional body is separated from the subject by a **blank line**, wrapped at 72 characters, phrased to say **why** the change exists — not what the diff already shows. Footers live after a final blank line and carry traceable references: `refs ADR-NNNN` when the commit enacts a decision documented in an ADR, `refs #123` for a ticket. A breaking change of external contract is flagged either by a `!` after the type or scope (`feat!:`, `refactor(core)!:`) or by a `BREAKING CHANGE: …` footer — the two are equivalent in the spec; Deckle does not yet have a publicly consumed release, so this mostly serves to flag what will need to surface at the time of a 1.0.

## Closed vocabulary of types

Eleven admitted types, aligned with the standard. **`feat`** introduces a new feature or user-facing behavior. **`fix`** fixes a bug. **`refactor`** changes internal structure without modifying observable behavior. **`docs`** exclusively touches documentation. **`test`** adds or modifies tests. **`perf`** improves a measurable performance. **`style`** corrects formatting without logic. **`build`** modifies the build system, dependencies, packaging scripts. **`ci`** modifies continuous integration (Deckle does not have one yet, but the type stays reserved). **`chore`** is the receptacle for maintenance that does not fit elsewhere (`.gitignore`, config files, housekeeping). **`revert`** undoes a prior commit.

One local type kept: **`merge`** for the merge commits of feature branches into `main`, in the form `merge: <branch-name> — <short summary>`. Preserves the readability of merges flat in `git log --oneline`. It is an assumed deviation from the Conventional Commits spec, justified by the project workflow (`--no-ff` on feature branches).

**Types to proscribe** because they emerged ad hoc and fragment the vocabulary: `prep`, `tune`, `tools`, `bench`, `tweak`, `hud`, `settings`, `engine`, `logs`. These intents all fall into `feat`, `refactor`, `chore` or `docs`. For benchmark iterations, the right format is `chore(bench): iteration N — …` — the scope carries the context, not the type.

## Scopes

The scope reflects the **boundary touched**, not the author or the environment. It is the short name of the module touched — the capability segment of its identifier, lowercased: `Deckle.Lighting.Ambient` → `ambient`, `Deckle.Transcription` → `transcription`, `Deckle.Core` → `core`. The live module list is the code and `TREE.md`, never a roster frozen here; naming follows `deckle-nomenclature`, and the grain (module vs sub-project) follows `deckle-modularite`. Three cross-cutting scopes cover commits that touch a project boundary rather than a module: **`scripts`** for `scripts/`, **`docs`** for `docs/` at the root (redundant with the type `docs:` only to disambiguate a precise page), **`agent`** for the `CLAUDE.md` files and the skills under `.claude/`.

**One single scope per commit.** The comma-separated form `feat(playground, ambient): …` that appeared in history is a splitting signal: either the commit blends two intents and must be split, or the actual scope is a cross-module theme (`refactor(observability)`, `refactor(catalog)`) that must be named. If a cross-cutting thematic scope starts appearing repeatedly, it is a signal to promote it to a dedicated sub-namespace — see `deckle-modularite`.

## Grain doctrine — when to split

A commit must be summarizable by **one sentence without `and` or `+`**. The presence of a `+` in the subject is the most reliable signal of a disguised megacommit: `chore: gitignore cleanup + untrack docs/archives` is two commits, `refactor(playground): States/Primitive sections + native Play/Pause toggle` is two commits. Each intent must be able to live and be reverted alone.

Canonical cases per workstream typology. **Cross-cutting overhaul such as the EventSource migration** — one infrastructure commit (interfaces, base class, boot registration), then one commit per migrated module (clear intent: migrate this module), then one commit switching the legacy sinks, then one commit cleaning up the stubs. No final megacommit that piles everything up. **Bug fix** — one commit for the fix, possibly one commit for the tests if coverage is added jointly. If the fix exposes a prerequisite refactor, the refactor is a separate commit upstream. **UI overhaul** — one commit per refactored surface, never an end-of-day dump. The UX copy pass on a page and the structural overhaul of the same page are two commits. **Renaming a module or an exposed symbol** — one commit for the rename alone (`refactor(catalog): rename Localization → Catalog`), then the functional content; this discipline makes the rename visible and spares it from a revert that would cancel real work.

When a workstream spans several modules, one test arbitrates the grain: **do the intermediate commits each leave the repo compiling and coherent?** If yes, split by cross-module step — one commit per migrated module, scoped to that module (the EventSource case above). If no — the operation is semantically indivisible, typically an atomic rename of a public symbol consumed everywhere where any split would produce a non-compiling state — collapse it into a single commit under a cross-module thematic scope (`refactor(observability)`, `refactor(catalog)`). That single commit stays the minimal semantic unit, not a dump.

## Grain doctrine — when to merge

The counterpart exists: a change is not atomic because it is small, it is atomic because it **forms a self-contained testable unit**. Three legitimate fusion cases. **Signature and callers** — modifying the signature of a public method and propagating the calls in the same commit, because an intermediate commit would not compile. **Resource and consumption** — adding a `.resw` key and consuming it in the matching XAML, because the orphan key has no meaning in isolation. **File rename and references** — moving a file and updating its `using` directives, because the repo does not hold together between the two.

A modification of foreign scope that slipped into a commit in progress **does not fuse opportunistically**. You undo with `git restore --staged` or `git reset`, you commit the main intent, then you commit the incidental modification separately.

## Merge commits

Project strategy: feature branches merged into `main` with `--no-ff`, never squash rebase. The merge commit receives as message `merge: <branch-name> — <short summary of the branch intent>`. The short summary is the cover sentence readable in `git log --oneline`; the internal commits of the branch stay visible via `git log <branch>` and are the raw material of bisectability.

The quality of a merge commit is **derived from the internal discipline of the branch**. If the internal commits are themselves compound or ambiguous dumps, no merge summary remedies that. The doctrinal responsibility is upstream, in each individual commit of the feature branch.

## Author identity

All commits go out under the identity of the maintainer (`Louis <git@louisfifre.com>`). **Never** a `Co-Authored-By: Claude <…@anthropic.com>` trailer, **never** a `🤖 Generated with [Claude Code](…)` line. These markers register Claude as a visible GitHub contributor, which is factually false: an LLM agent is not a contributor in the version-control sense. The rule is carried by the project's root `CLAUDE.md`; it is restated here because it is precisely the act of committing that puts it at stake, and that is the moment when the temptation to inscribe the agent reappears.

## Three audit signals before sending

Before executing `git commit`, three review questions that catch the majority of drifts observed in the Deckle history. **Does the subject contain a `+` or an `and`** that joins two distinct intents? Split. **Does the subject exceed 72 characters without any intent being removable**? It is probably two commits camouflaged as one. **Is the scope comma-separated** or imprecise (`(playground, ambient)`, `(misc)`)? Pick the main scope and split the other intent, or name a legitimate thematic scope.

## Pointers

- **`personal-conventions`** — cross-project rules (language, branch conventions, worktrees). `deckle-commits` applies them to the project.
- **`session-save-context`** — routing for durable-value information including ADRs. When a commit enacts a tracked decision, the body mentions `refs ADR-NNNN`.
- **`deckle-modularite`** — module boundaries and the grain (module vs sub-project) a scope reflects.
- **`deckle-nomenclature`** — naming vocabulary, including the module names that serve as scopes.
- **[conventionalcommits.org](https://www.conventionalcommits.org/en/v1.0.0/)** — normative reference spec.
