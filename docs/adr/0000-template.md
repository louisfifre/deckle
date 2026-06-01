---
name: adr-0000-template
description: "Fill-in template and worked example for Deckle ADRs. Not a decision — copy it to start one. Defers the format rules to session-save-context/format.md and the warrant test to grill-with-docs/ADR-FORMAT.md."
type: adr
---

# ADR-0000 — Template (copy me; record no decision here)

**Status** — template (never accepted)

> In-repo example of the Deckle ADR shape. Copy this file to `NNNN-kebab-title.md`,
> take the next free number, fill the four sections, delete this blockquote.
>
> - Frontmatter, closed H2 vocabulary, file naming, RFC 2119 wording → [`session-save-context/format.md`](../../.claude/skills/session-save-context/format.md).
> - When an ADR is warranted — **hard to reverse · surprising without context · a real trade-off** — → `grill-with-docs/ADR-FORMAT.md`.
> - What is *not* an ADR — obvious choices visible in the code, temporary states, workarounds, POCs, experiments. The value of an ADR is the *why*, never the *what*.
> - The log is append-only: never edit an accepted ADR. A reversed decision is a *new* ADR that supersedes the old one (`**Status** — superseded by ADR-NNNN`), the two linked.

## Context

The situation forcing a decision; the constraints; the why. Written for a reader who lacks the conversation that produced it.

## Options considered

- **A. …** — what it buys, what it costs.
- **B. …** — …
- **C. …** — …

## Decision

The retained option, stated plainly. MUST / SHOULD / MAY for normative weight. Record a low confidence level when the decision is made with one — it helps future reconsideration.

## Consequences

What becomes easier, harder, impossible. Reversibility and re-evaluation conditions when they exist.
