---
name: deckle-journal
description: Journal doctrine for Deckle: how the root and per-module JOURNAL.md chronicles are written — one entry per closed milestone, the admitted content, the epistemic marking a public record demands, the reversibility that distinguishes it from ADRs. Invoke before a journal entry, when judging whether a milestone deserves one, or when promoting one to an ADR or doctrine. Triggers like deckle journal, journal entry, devlog, JOURNAL.md, milestone entry.
type: skill
---

# Deckle — Journal doctrine

## Role

Answers: **which entry, with which content, in which journal**. The dated chronicle complements the ADRs without competing — ADRs freeze tranched decisions, the journal carries the in-between (validated advances, dated hypotheses, milestones, methodological learnings), versioned and public in the repo. Invoked before writing an entry, when judging whether a milestone deserves one, and when promoting an entry to a stable artefact.

Two scopes. **Project journal** — `JOURNAL.md` at the repo root: cross-module milestones, posture shifts, transverse observations. **Module journal** — `<module>/JOURNAL.md`: the chronicle specific to that module, created only when its work generates dated material worth keeping, never preemptively. The reference implementation, written before the doctrine crystallized, is [`benchmark/JOURNAL.md`](../../../benchmark/JOURNAL.md) — normative for both scopes.

## Granularity

One entry equals **one closed technical milestone** — a feature delivered, a refactor merged and verified, a bug investigated with verdict, an integration brought to a measurable state, a workstream pivot taken on evidence. Not "everything done in the session": if no milestone closed, no entry — the work continues, the chronicle stays silent. When several milestones close the same day, suffix `## YYYY-MM-DD (suite)`, `(suite 2)`; each suite entry stays self-contained.

## What goes in

Four kinds of content. **Validated advances** — delivered, merged, fixed-with-confirmation, measured; stated as fact. **Observations as hypotheses** — a behavior noticed, a signal unexplained; always marked as such (see *Epistemic rigor*). **Usage feedback** — how a feature felt in real use, distinguishing what was measured from what was felt. **Methodological learnings** — what worked or failed about how we worked; when one stabilizes, it is promoted into the relevant `CLAUDE.md`.

## What does not go in

**Tranched decisions** with alternatives weighed → an ADR under `docs/adr/`; the journal may point (`voir ADR-NNNN`) but does not restate. **Frozen investigation material** → `docs/research/research--slug--YYYY-MM-DD.md`; the journal narrates and points to it. **Sensitive content** (credentials, stakeholder names, financials) → private auto-memory or unwritten; the journal is a public commit, so when unsure whether something belongs, ask Louis. **Catch-all session dumps** — the journal is selective, not comprehensive; work with no closed shape gets no entry. This is the same anti-dump rule `session-save-context` enforces at the routing level.

## Epistemic rigor

The journal is a public record; every assertion must let a future reader recover its status.

- **Validated** — confirmed by test, measurement, or repeated real use. Stated as fact. *« la WER médiane BF16 passe de 0.447 à 0.257 sur 30 samples ».*
- **Hypothetical** — observed once, untested, or inferred. Marked explicitly — « observation à confirmer », « semble que », « probablement », or the English "to confirm", "appears to". *« le 3B Q8_0 semble plus sensible au mode chat sur les samples courts — à confirmer ».*
- **Tranched** — a decision weighed against alternatives. The journal may record the verdict (« tranché : Q8_0 abandonné »), but if the reasoning is weighty the ADR is owed and the entry points to it.

The reliable drift signal is an unqualified statement about something not yet measured.

## Entry format

Header `## YYYY-MM-DD — Titre court synthétique` (a noun phrase naming the milestone); same-day suites `(suite)`, `(suite 2)`. Body in short prose paragraphs, **bold sub-titles** as lead-ins when useful (« **Mesures objectives** », « **Direction prochaine session** »), tables when measurements are compared, explicit markdown pointers when references exist (commit short SHA, ADR path, research path, URL). Recent entries on top, each separated by a `---` rule; the preamble stays at the top, never reordered. Length is whatever the milestone deserves — no imposed minimum or maximum.

## Reversibility

Unlike ADRs, journal entries may be edited, refactored, or archived. A hypothesis confirmed or refuted elsewhere, an observation later explained by an ADR — edit in place or remove. A hypothesis that stabilizes into a durable decision is promoted to an ADR, the entry becoming a pointer. The journal is a living chronicle, not a frozen archive. This is the inverse of the ADR rule: if a piece of information would suffer from being rewriteable later, it belongs in an ADR, not here.

## Articulation with neighboring artefacts

- **ADR** — a decision tranched and frozen, supersedable only by a new ADR. A journal entry recording one is promoted to an ADR and becomes a pointer.
- **`docs/research/`** — frozen raw matter, one dated file per investigation, not edited after creation; the journal references it.
- **project / module `CLAUDE.md`** — timeless doctrine. A learning that stabilizes is lifted from the journal into the matching `CLAUDE.md`, the entry becoming a pointer.
- **auto-memory** — private, out of repo. Sensitive content lives there; published advances live in the journal.

## Frontmatter

Every JOURNAL.md carries:

```yaml
---
name: journal-<scope>
description: <one line — what the journal covers + when it is read>
type: project-journal | module-journal
---
```

`name`: `journal-deckle` for the project journal, `journal-<module>` otherwise (`journal-benchmark`, `journal-hud`). The closed type list in `session-save-context/format.md` admits `project-journal` and `module-journal`.

## Pointers

- **[`examples.md`](./examples.md)** — worked entries: a validated advance, a hypothesis, a learning ready to promote. Read when shaping an entry.
- **[`benchmark/JOURNAL.md`](../../../benchmark/JOURNAL.md)** — the reference module-journal; normative form.
- **[`JOURNAL.md`](../../../JOURNAL.md)** — the project journal at the repo root.
- **`session-save-context`** — the routing cascade for one-shot durable info; the journal runs parallel to it (a continuous chronicle, not a routing destination).
- **`deckle-commits`** — point an entry to its commits (short SHA + subject); same maintainer-voice identity, no LLM co-author trailers.
- **`personal-conventions`** — the author-identity rule (no LLM co-signature) applies to the journal too.
