---
name: deckle-commits
description: Commit grain and the few deviations from the universal convention. Invoke before committing, splitting a workstream into commits, or auditing history.
---

# Deckle — Commits

## Intent

Land the right commit grain and apply the handful of deviations from the universal convention. The universal convention lives in `personal-conventions/references/git.md`; this carries only what Deckle adds on top.

## How

One commit = one self-contained intent — split aggressively, and each split must leave the repo compiling and coherent.

A scope names the module touched, one per commit; every change folds into the universal set of types, plus `deprecate`, which announces a capability the next MAJOR removes. A revert written by hand carries git's `This reverts commit <sha>` line in its body.

Author identity — maintainer alone, never a `Co-Authored-By: Claude` trailer or a `🤖 Generated with…` line. The temptation reappears exactly at commit time; it must never come back.
