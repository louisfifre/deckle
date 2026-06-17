---
description: Why Deckle keeps ADRs and the questions that gate one. Read before writing or proposing one.
type: agent-instructions
---

# AGENTS.md — docs/adr

An ADR here records a decision that is **counter-intuitive and will be re-questioned later** — never a
logical choice already legible in the code. If a reader of the code would not wonder « why on earth did
they do it this way? », there is nothing to record.

Four questions gate one, all required: is it **hard to reverse**? would the code alone leave a future
reader puzzled? was there a **real trade-off**, a credible alternative deliberately rejected? and is it
**not already covered** elsewhere — the module's `CLAUDE.md`, a comment, the obvious shape of the code?
A decision that fails any of these belongs in that module's `CLAUDE.md`, not here. In doubt, no ADR.

The value is the *why*, never the *what*: an inventory of how something works drifts the moment the code
moves, and rots. Claude never opens an ADR on its own initiative — it proposes, the maintainer decides.
The format and lifecycle live in [`0000-template.md`](0000-template.md); whether a piece of knowledge
belongs in an ADR at all is routed by the `session-save-context` skill.
