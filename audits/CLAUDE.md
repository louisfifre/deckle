---
name: audits-agent-instructions
description: "Shared instructions for recurring Deckle audits run by Codex, Claude, or future local agents."
type: agent-instructions
---

# CLAUDE.md — Deckle Audits

## Role

This folder defines the recurring audit routine for Deckle. The audit agent acts as a reviewer, not as an implementer: it inspects repository state, doctrine alignment, security posture, architecture drift, testing debt, observability coverage, and documentation drift.

The root [`../CLAUDE.md`](../CLAUDE.md) remains authoritative. If this folder conflicts with root or module doctrine, follow the authoritative project source and report the conflict in the audit.

## Context

An audit is a dated snapshot. It is useful when it makes change over time visible: new risk, resolved risk, repeated weak signal, missing test, doctrine drift, or release-readiness concern.

Codex and Claude SHOULD use the same schema so their outputs can be compared directly. Agent-specific prompts MAY tune emphasis, but they MUST NOT change the report contract.

## Doctrine

Before writing a run, the agent MUST read:

- root `CLAUDE.md`;
- root `CONTEXT.md`;
- root `SECURITY.md`;
- `audits/schema.md`;
- the latest prior run for the same agent, when one exists;
- module `CLAUDE.md` files for modules touched since the prior run.

The agent SHOULD inspect recent changes with Git before drawing conclusions. The default comparison base is the latest previous audit run; if none exists, use the current repository state as the baseline.

The agent MAY run `dotnet build` or `dotnet test` when the audit goal justifies it. A command result is evidence, not the whole audit.

Each section MUST be concise: at most three short sentences unless a concrete high-severity finding needs detail. Prefer precise observations over generic quality advice.

## Pointers

- [`schema.md`](schema.md) — required report structure and closed vocabulary.
- [`templates/weekly-audit.md`](templates/weekly-audit.md) — copyable starting point for a run.
- [`prompts/codex.md`](prompts/codex.md) — Codex-specific routine prompt.
- [`prompts/claude.md`](prompts/claude.md) — Claude-specific routine prompt.
- [`index.csv`](index.csv) — compact inventory of runs and headline statuses.
