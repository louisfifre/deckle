---
name: deckle-commits
description: Use when you commit in Deckle, split a workstream into commits or read its history, to choose each commit's grain, type and scope.
---

## Why

The global git convention, `personal-conventions/references/git.md`, leaves the grain, the types and the scopes to each project. Deckle sets them here and restates nothing else.

## Statement

A commit carries one intent that its subject can name. A workstream splits into as many commits as it has intents, and each commit leaves the repository compiling and coherent.

A commit has one scope, the module or repository area it touches. A given module always takes the same scope.

The type comes from the Conventional Commits set the Angular convention lists, feat, fix, docs, style, refactor, perf, test, build, ci, chore and revert, and no type is invented beyond it. The type decides whether the change reaches the changelog, and `deckle-versioning` says which types do.
