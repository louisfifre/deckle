---
name: deckle-journal
description: Journal doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries the way technical chronicles are written — the project-level JOURNAL.md at the repo root and the optional per-module JOURNAL.md files. Covers the granularity of one entry equals one closed milestone, the closed perimeter of admitted content (validated advances, observations marked as hypotheses, usage feedback, methodological learnings), the epistemic rigor required for a public record, the articulation with ADRs and dated research, the reversibility assumed unlike ADRs. Invoked before every journal entry, when deciding whether a milestone deserves an entry, and when promoting an entry to a stable artefact (ADR or CLAUDE.md doctrine). Triggers on phrases like deckle journal, entrée journal, journal entry, devlog deckle, journal session deckle, journal benchmark, JOURNAL.md, milestone journal deckle.
type: skill
---

# Deckle — Journal doctrine

## Role

Project-specific skill that answers: **which entry, with which content, in which journal**. The dated chronicle complements the ADRs without competing with them. ADRs freeze tranched decisions; the journal carries the in-between matter — validated advances, dated hypotheses, milestones reached, methodological learnings — versioned and public in the repo. Invoked before writing an entry, when deciding whether a milestone deserves one, and when promoting an entry to a stable artefact.

Two scopes coexist. **Project journal** lives at the repo root as `JOURNAL.md` — carries cross-module milestones, project posture shifts, transverse observations. **Module journal** lives at `<module>/JOURNAL.md` — carries the chronicle specific to that module. The reference implementation written organically before the doctrine crystallized is [`benchmark/JOURNAL.md`](../../../benchmark/JOURNAL.md); the form observed there is normative for both scopes.

## Granularity

One entry equals **one technical milestone closed**. Not "everything done in the session". A milestone is a piece of work that bounded itself: a feature delivered, a refactor merged and verified, a bug investigated with verdict, an integration brought to a measurable state, a workstream pivot taken on evidence. If a session produced no closed milestone, no entry — the work continues, the chronicle stays silent. The journal is selective by design.

Multiple entries per day are admitted when multiple milestones close. The date is suffixed `## YYYY-MM-DD (suite)`, `## YYYY-MM-DD (suite 2)`, etc. — convention observed in `benchmark/JOURNAL.md`. Each suite entry stays self-contained; readers SHOULD be able to grasp one without reading the previous.

## What goes in

**Validated technical advances.** Feature delivered, refactor merged, bug fixed with confirmation, integration mesured. The default content of the journal. Stated as fact.

**Observations in progress marked as hypotheses.** A remarked behavior, an unexplained signal, a pattern noticed once. MUST be explicitly framed as hypothesis — French markers: « observation à confirmer », « soupçon non investigué », « hypothèse à tester », « semble que », « probablement »; English equivalents when the journal is in English: "to confirm", "untested hypothesis", "appears to". Never the unqualified affirmative form for something not yet measured.

**Usage feedback.** How a delivered feature felt in real use — latency, friction, shortcut ergonomy, sensory quality. MUST distinguish what was measured (mesured RTF, mesured WER, p95 latency) from what was felt subjectively. Both have value, but mixing them silently misleads.

**Methodological learnings.** What worked or failed about the way we worked — a research agent strategy that converged, a diagnostic approach that wasted hours, a vigilance trigger discovered. When the learning stabilizes, it gets promoted into the relevant `CLAUDE.md` doctrine.

## What does NOT go in

**Tranched decisions with alternatives weighed.** These go to an ADR under `docs/adr/`. The journal MAY mention « ADR-NNNN ouvert » or « décision tranchée, voir ADR-NNNN » as a pointer, but it does not restate the decision.

**Frozen investigation material.** A single research output — a study, a comparison table, an external doc imported for reference — goes to `docs/research/research--slug--YYYY-MM-DD.md`. The journal narrates the chronicle and points to the research; the research file freezes the raw matter.

**Sensitive content.** Credentials, secrets, internal stakeholder names, financial details, raw drama. The journal is committed to a public GitHub repo. Anything that does not belong on a public commit stays in auto-memory (private, out of repo) or stays unwritten.

**Catch-all session dumps.** « Here's everything I did today » is the antipattern. The journal is selective, not comprehensive. If a piece of work has no closed shape — no milestone reached, no decision tranched, no observation worth a hypothesis tag — it does not get an entry. This is the same antipattern that `session-save-context` combats at the routing level: no fallback dump, ever.

## Epistemic rigor

The journal is a public record. Every assertion carries an implicit epistemic status that the reader must be able to recover.

**Validated.** Confirmed by automated test, by measurement, or by repeated real-world usage. Stated as fact without hedging. Example: « la WER médiane BF16 sur 30 samples passe de 0.447 à 0.257 » — a measurement.

**Hypothetical.** Observed once, untested, or inferred. MUST be framed explicitly. Example: « le 3B Q8_0 semble plus sensible au mode chat sur les samples courts — hypothèse à confirmer sur un échantillon plus large ».

**Tranched.** A decision was taken with alternatives weighed. The journal MAY record the verdict (« tranché par Louis : Q8_0 abandonné »), but if the alternatives and reasoning are weighty, the ADR is owed and the journal entry points to it.

A future reader, sentence by sentence, MUST be able to tell whether what's written is established fact, observed pattern, or working hypothesis. The most reliable drift signal is the use of unqualified statements about things not yet measured.

## Entry format

Header line: `## YYYY-MM-DD — Titre court synthétique`. Title is a short noun phrase naming the milestone. Same-day suites: `## YYYY-MM-DD (suite) — …`, `## YYYY-MM-DD (suite 2) — …`.

Body is short prose paragraphs. Thematic blocks MAY use **bold sub-titles** as lead-ins (« **Mesures objectives** », « **Pitfall stack actée** », « **Direction prochaine session** »). Tables are welcome when measurements are compared. Explicit markdown pointers when references exist — commits SHA short form, ADR paths, research file paths, external URLs.

Recent entries on top. New entry inserted just below the preamble, separated by a `---` horizontal rule from the previous one. The preamble itself stays at the top, never reordered.

Length is whatever the milestone deserves — one paragraph for a clean fix, several paragraphs and tables for a substantial pivot. No imposed minimum nor maximum.

## Reversibility

Unlike ADRs, journal entries MAY be edited, refactored, or archived. If an entry becomes stale (a hypothesis confirmed or refuted elsewhere, an observation explained by a later ADR), edit it in place or remove it. If a hypothesis stabilizes into a durable decision, promote it to an ADR and update the journal entry to point to the ADR. The journal is a living chronicle, not a frozen archive.

This reversibility is the inverse of the ADR rule. An ADR cannot be modified once accepted; a journal entry can. If a piece of information would suffer from being rewriteable in the future, it is in the wrong place — it belongs in an ADR.

## Articulation with neighboring artefacts

**vs ADR.** ADR = decision tranched with alternatives weighed, frozen, supersedable by a new ADR. Journal = chronicle, including reversible hypotheses. A journal entry that records a tranched decision SHOULD be promoted to an ADR; the entry then becomes a pointer.

**vs `docs/research/`.** Research = frozen raw matter, one file per investigation, dated in the filename. Journal = continuous narration, one file appended over time. A research output is referenced from the journal; the research file is not edited after creation.

**vs project/module `CLAUDE.md`.** CLAUDE.md = timeless durable doctrine. Journal = dated chronicle. A learning that stabilizes into a durable rule MUST be lifted from the journal into the corresponding CLAUDE.md; the journal entry then becomes a pointer to where the rule now lives.

**vs auto-memory.** Memory = private, out of repo, scoped to the maintainer's machine. Journal = public, in repo, committed. Sensitive content belongs in memory; published advances belong in the journal.

## Frontmatter

Every JOURNAL.md MUST carry this frontmatter shape:

```yaml
---
name: journal-<scope>
description: <one line — what the journal covers + when it is read>
type: project-journal | module-journal
---
```

`name` follows the slug pattern: `journal-deckle` for the project journal, `journal-<module>` for module journals (`journal-benchmark`, `journal-hud`, etc.).

The closed list of frontmatter types in `session-save-context/format.md` (global skill under `~/.claude/skills/session-save-context/`) admits `project-journal` and `module-journal` as legitimate values.

## Pointers

- **[`benchmark/JOURNAL.md`](../../../benchmark/JOURNAL.md)** — the reference module-journal, written organically before the doctrine crystallized. Normative form for both scopes.
- **[`JOURNAL.md`](../../../JOURNAL.md)** at the repo root — the project journal.
- **`session-save-context`** — routing cascade for ponctual durable information. The journal is parallel to the cascade rather than inside it: a chronicle is a continuous usage, not a routing destination for one-shot info.
- **`deckle-commits`** — when a journal entry records a milestone, the corresponding commits SHOULD be pointed to (short SHA + subject).
- **`personal-conventions`** — the author identity rule that excludes LLM co-signature trailers applies to the journal too: entries go out under the maintainer's voice.

## Boundaries

**Always do:**
- Date entries `YYYY-MM-DD`, recent on top, with `(suite N)` for same-day extra entries.
- Mark hypothesis vs validated vs tranched explicitly in prose.
- Add explicit pointers (commit SHA, ADR-NNNN, research file path) when references exist.
- Promote a stabilized learning into the relevant CLAUDE.md, a stabilized decision into an ADR. Update the journal entry to point to the new home.
- Write under the maintainer's voice — same identity rule as for commits.

**Ask first:**
- If an entry would touch sensitive content (stakeholder names, financial figures, security details), ask Louis whether it belongs in the public journal or in private memory.
- If uncertain whether a milestone deserves an entry or should stay silent, ask. Silence is a legitimate choice for ongoing unbounded work.
- If two milestones close on the same day and you're unsure whether to write one combined entry or two suite entries, ask.

**Never do:**
- Dump a comprehensive session report. The journal is selective.
- Affirm as fact what has not been validated by test, measurement, or repeated usage. Mark as hypothesis.
- Duplicate an ADR. The journal points to ADRs, never restates them.
- Add « Co-Authored-By: Claude » or « 🤖 Generated with Claude Code » trailers. The journal is the maintainer's voice, same as commits.
- Create the journal preemptively for a module that has no chronicle worth tracing. A module-journal exists when its work generates dated material worth keeping; otherwise it doesn't.

## Examples

**Validated technical advance.**

> ## 2026-05-27 — Refonte format artefacts agents (ADR-0013)
>
> Adoption du format canonique unifié pour tous les artefacts agents — `CLAUDE.md`, `SKILL.md`, ADRs, sheets `reference`/`research`, module READMEs. Le frontmatter YAML devient obligatoire (`name`, `description`, `type`), le vocabulaire d'H2 est fermé (Role, Context, Doctrine, Pointers, Boundaries, Examples), la convention RFC 2119 (MUST/SHOULD/MAY) cadre les paragraphes prescriptifs. Migration complète des artefacts existants livrée dans le merge `docs/refonte-format-artefacts-agents` ([c58a303](commit-sha)).
>
> Référence : [ADR-0013](docs/adr/0013-format-canonique-des-artefacts-agents.md). Le format normatif vit dans [`session-save-context/format.md`](.claude/skills/session-save-context/format.md).

**Observation in progress (hypothesis).**

> ## 2026-MM-DD — Bug intermittent sur le tray menu post-rebuild
>
> Pillule custom du tray menu disparaît sporadiquement après un rebuild complet, sans pattern reproductible identifié à ce stade. Soupçon initial : timing de chargement du `Style` custom vs activation du flyout. Hypothèse à confirmer en instrumentant le cycle de vie du flyout sur prochaine occurrence. Flagger ne suffit pas — la pillule sera ré-observée dans `Deckle.Shell` avant patch.

**Methodological learning ready to promote.**

> ## 2026-05-27 (suite) — Diagnostic vieillissant et règle « official sources first »
>
> Le pivot de stack `transformers + torch ROCm` → `torch-directml` de fin mai reposait sur un bug d'import `torch.distributed.tensor` qui avait été guardé upstream 9 mois plus tôt par [transformers PR #40038](https://github.com/huggingface/transformers/pull/40038). Le diagnostic interne avait vieilli silencieusement. La règle écrite en doctrine cross-project du `CLAUDE.md` racine (« Official sources first on a moving tech ») et appliquée par les agents recherche du 2026-05-27 a permis de retomber sur la voie viable.
>
> À promouvoir : cette règle est désormais explicite dans le `CLAUDE.md` racine, l'entrée journal devient un pointeur.
