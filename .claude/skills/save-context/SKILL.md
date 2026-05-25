---
name: save-context
description: When in-session information has durable value (a tranched decision, learned technical doctrine, research output, resolved terminology), routes it to the right on-disk home through a fixed cascade — ADR, module CLAUDE.md, project CLAUDE.md, dated research, external reference, CONTEXT.md, memory as fallback. Neutralizes the "context blob" antipattern. Louis invokes it manually, or it triggers naturally when information with durable value appears. Triggers on phrases like sauvegarde le contexte, sauvegarde cette info, save context, save this info, persist this, route this info, where do I put this info, on grave ça où.
type: skill
---

## Context

Antipattern to neutralize: when an agent senses it will lose context, it dumps a catch-all "plan" or "phase" document to self-recharge later. These blobs pollute the repo, mix decisions, research, and drafts, and never get found again. The alternative discipline: *if a piece of information deserves preservation, it has a known category, and the category has a normed home on disk*. The repo is not a fallback dump — it is a typed structure.

## Doctrine — routing cascade

When information to preserve is identified, apply the cascade in order. Pick the **first route that matches**; never write to multiple homes.

**Route 1 — Tranched decision.** The information names a choice between alternatives, with a *why*, and the decision is hard-to-reverse. Home: `docs/adr/NNNN-kebab-title.md`. Number after the largest existing `NNNN`, never reused even after superseded. Minimal Nygard format — Context, Options considered, Decision, Consequences, Status accepted on `YYYY-MM-DD`. See `docs/adr/0001-record-architecture-decisions.md` for the canonical template.

**Route 2 — Technical doctrine on a module.** The information is a durable internal rule of module X (known pitfall, antipattern to refuse, calling convention, canonical pattern). Home: `src/Deckle.X/CLAUDE.md`, `Doctrine` section. Updated in place, no versioning. If the module lacks a `CLAUDE.md`, create it with its frontmatter (see [format.md](./format.md)).

**Route 3 — Cross-module project doctrine.** The information concerns several modules or the project posture (non-negotiable rule, product scope, transverse platform constraint). Home: `<repo-root>/CLAUDE.md`. This file is always in session context, so only put here what MUST be present at *every* session. Keep it dry.

**Route 4 — Dated investigation note.** The information is raw material from a research, comparison, or exploration — not a decision, not doctrine. Home: `docs/research/research--slug--YYYY-MM-DD.md`. First line as blockquote to mark provenance if third-party (`> Source: ChatGPT GPT-5 · 2026-MM-DD`).

**Route 5 — Frozen external reference.** The information is text produced by a third party (upstream doc, frozen output from another LLM, reference article) kept on hand to cite later from an ADR or another sheet. Home: `docs/reference/<slug>.md`. Provenance blockquote on first line.

**Route 6 — Project terminology.** The information resolves a vocabulary ambiguity — a term in Deckle means X, not Y. Home: `CONTEXT.md` at the repo root. Dedicated section for the concept, short prose, never implementation or doctrine.

**Route 7 — No disk home matches.** If the information fits none of routes 1-6, MAY write to auto-memory under `~/.claude/projects/D--projects-ai-deckle/memory/`. Add the marker `[review needed: disk route uncertain]` in the memory description so a future consolidation pass examines it and promotes it if a route emerges.

## Format

Every file created or modified by this skill MUST carry a conformant YAML frontmatter and pull its H2 sections from the closed vocabulary. Normative reference: [format.md](./format.md).

## Boundaries

**Always do:**
- Apply the cascade in order; take the first route that matches.
- Verify the frontmatter before writing (`name`, `description`, `type`).
- For an ADR, compute the next `NNNN` by scanning `docs/adr/`.
- For dated research, use today's ISO date.

**Ask first:**
- If the information matches two routes equally (e.g. decision + cross-module doctrine), ask Louis which prevails.
- If the target home is ambiguous (e.g. "doctrine for module X or neighboring module Y"), ask Louis.
- Before promoting a `[review needed]`-flagged memory entry to disk, ask for confirmation.

**Never do:**
- Create a new folder under `docs/`. The taxonomy is fixed: `adr`, `reference`, `research`.
- Add anything to `CONTEXT.md` other than a vocabulary term — no implementation, no doctrine, no plan.
- Dump a "plan", "phase", "context-save", or "session-notes" document as fallback. If nothing matches routes 1-6, fall back to memory or ask Louis — never invent a new file type.
- Modify an already-accepted ADR. A revised decision creates a new ADR that supersedes the old one.

## Pointers

- [format.md](./format.md) — YAML frontmatter, closed section vocabulary, RFC 2119. Normative reference reusable by other skills.
- [Project CLAUDE.md](../../../CLAUDE.md) — carries the automatic trigger for this skill.
- [docs/adr/0001-record-architecture-decisions.md](../../../docs/adr/0001-record-architecture-decisions.md) — canonical Nygard ADR template.

## Examples

**Tranched decision → ADR.**

> Louis: "OK, sticking with whisper.cpp for now, we'll check Voxtral when it's more mature."
> Skill: info = decision with discarded alternative and reason. Route 1. Creates `docs/adr/0007-stay-on-whisper-cpp-watch-voxtral.md` with frontmatter `type: adr`, Nygard body.

**Technical doctrine on a module → module CLAUDE.md.**

> Louis: "In Deckle.Hud, HudOverlayManager must always create its windows lazily, otherwise we crash at boot."
> Skill: info = durable rule for the Hud module. Route 2. Adds the rule to the `Doctrine` section of `src/Deckle.Hud/CLAUDE.md`.

**External investigation material → research.**

> Louis: "Found a comparison of HDR interpolators on this gist, I want to keep it."
> Skill: info = raw third-party material, not a decision. Route 4. Creates `docs/research/research--hyperhdr-interpolators--2026-05-25.md` with provenance blockquote and the imported content.

**Resolved terminology → CONTEXT.md.**

> Louis: "In Deckle, when we say *integration test*, it means with a natural seam — not a parasitic seam created just for testing."
> Skill: info = project vocabulary refinement. Route 6. Updates the testing-categories section of `CONTEXT.md`.

**Uncertain-value info → memory with flag.**

> Louis: "I noticed the Pause+F12 hotkey doesn't trigger on my current machine. To investigate."
> Skill: info = isolated observation without a stable home. Not a decision, not doctrine, not structured research. Route 7. Writes to auto-memory with description `[review needed: disk route uncertain] Pause+F12 hotkey not triggering on current machine, cause unknown.`
