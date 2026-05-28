---
name: audits-schema
description: "Canonical schema for Deckle recurring audit reports: frontmatter, section names, status vocabulary, and index fields."
type: agent-instructions
---

# Deckle Audit Schema

## Role

This file defines the structure of recurring Deckle audit reports. It is optimized for two readers: Louis scanning the report quickly, and agents comparing reports across time.

## Doctrine

Each run MUST be a Markdown file under `audits/runs/YYYY/` with this filename pattern:

```text
YYYY-MM-DD--agent.md
```

Allowed `agent` values are `codex`, `claude`, or a future lowercase kebab-case runner name.

The report MUST begin with YAML frontmatter:

```yaml
---
date: YYYY-MM-DD
agent: codex
scope: weekly-project-audit
baseline: previous-run
commit: short-sha-or-unknown
status: stable
---
```

`status` MUST use one of these values:

- `stable` — no meaningful concern found in this pass.
- `watch` — weak signal or area to revisit.
- `weak` — known gap that deserves planned work.
- `regression` — state got worse since the baseline.
- `blocked` — audit could not evaluate a required area.
- `not-reviewed` — intentionally skipped or out of scope.

Each body section MUST use the exact H2 headings below, in this order:

```md
## Summary
## Security
## Architecture
## Testing
## Observability
## UX Native Fit
## Documentation Drift
## Follow-ups
```

Each section MUST start with `Status: <value>.` using the same closed vocabulary. Each section SHOULD stay under three short sentences.

## Examples

A minimal section:

```md
## Security

Status: stable.
No new sensitive surface was found in the changed files. SECURITY.md still matches the observed repository posture.
```

## Index

`audits/index.csv` is a compact inventory, not the source of truth. Each run SHOULD add or update one row with these columns:

```csv
date,agent,commit,status,security,architecture,testing,observability,ux_native_fit,documentation_drift,report
```
