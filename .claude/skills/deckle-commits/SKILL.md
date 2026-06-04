---
name: deckle-commits
description: Commit grain and the few deviations from the universal convention. Invoke before committing, splitting a workstream into commits, or auditing history.
type: skill
---

# Deckle — Commits

## Intent

Land the right commit grain and apply the handful of deviations from the universal convention. The universal Conventional Commits mechanics — format, type table, staging, safety — live in `git-commit`; this carries only what Deckle adds on top.

## How

One commit = one self-contained intent — split aggressively, and each split must leave the repo compiling and coherent.

A scope names the module touched, one per commit; don't invent commit types, every change folds into the universal set.

Author identity — maintainer alone, never a `Co-Authored-By: Claude` trailer or a `🤖 Generated with…` line. The temptation reappears exactly at commit time; it must never come back.
