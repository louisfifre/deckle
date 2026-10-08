---
description: Why Deckle keeps ADRs and the questions that gate one.
type: agent-instructions
---

# AGENTS.md — docs/adr

An ADR records a decision that is **counter-intuitive and will be questioned again**. Never record a logical choice the code already makes legible. If a reader of the code would not ask « why on earth did they do it this way? », there is nothing to record.

Four questions gate an ADR, all required:

- Is it **hard to reverse**?
- Would the code alone leave a future reader puzzled?
- Was there a **real trade-off**, a credible alternative deliberately rejected?
- Is it **not already covered** elsewhere: the module's `AGENTS.md`, a comment, the obvious shape of the code?

A decision that fails any question belongs in that module's `AGENTS.md`. In doubt, no ADR.

Record the *why*, never the *what*: an inventory of how something works rots as soon as the code moves.

Claude never opens an ADR on its own initiative. It proposes; the maintainer decides.

The format and lifecycle are in [`0000-template.md`](0000-template.md). Whether knowledge belongs in an ADR at all is settled by the global `personal-conventions` skill: its context-elements reference says which element holds which kind of knowledge.
