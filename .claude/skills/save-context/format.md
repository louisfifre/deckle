# Agent artifact format — normative reference

This file is the normative reference for every agent artifact in the Deckle project: `CLAUDE.md` (root and per-module), module `README.md` files, skills under `.claude/skills/`, ADRs under `docs/adr/`, sheets under `docs/reference/` and `docs/research/`, spawn-task prompts. It is referenced by the [`save-context`](./SKILL.md) skill and by any other skill that produces or modifies an agent artifact.

## YAML frontmatter

Every agent markdown MUST carry a YAML frontmatter between `---` markers at the top of the file.

**Required fields:**

- `name` — kebab-case slug identifying the doc, unique within its category.
- `description` — one line: *what* the doc says + *when* to read or invoke it. For a skill, MUST also include plausible trigger phrases.
- `type` — one of: `agent-instructions`, `skill`, `adr`, `reference`, `research`, `module-readme`.

**Fields optional by `type`:**

- `module` — module name (`Deckle.X`) for module-scoped files.
- `version` — for versioned `reference` (`1.0`, `1.1`, etc.).
- `date` — for dated `research`, ISO `YYYY-MM-DD`.

The `scripts/update-tree.ps1` hook scrapes `name` + `description` + `type` and displays them next to each markdown in `TREE.md`.

## Closed vocabulary of H2 sections

Every agent artifact draws its H2 sections from the closed vocabulary below. No imposed skeleton: each artifact instantiates the sections it needs and omits the others. But when a section appears, it carries one of these canonical names — no free variation, no invented section.

- **Role** — who speaks, to whom, with what posture. Often implicit via the frontmatter or the filename. Rarely written explicitly.
- **Context** — the situation, the why, the upstream constraints framing the goal. Audience: an agent that lacks human context memory.
- **Doctrine** — normative rules, positive injunctions. Main section of skills and `CLAUDE.md`. RFC 2119 vocabulary MUST / SHOULD / MAY.
- **Pointers** — file paths, skills to invoke, references to read, MCPs to consult. Explicit markdown links, never "you already know".
- **Boundaries** — hard rules, structured in three subblocks: *Always do*, *Ask first*, *Never do*. Inherited from the AGENTS.md spec pattern.
- **Examples** — concrete scenarios, dialogs, before/after. Optional but powerful — an example anchors doctrine better than a definition.

## RFC 2119 normative vocabulary

In prescriptive paragraphs, use **MUST, MUST NOT, SHOULD, SHOULD NOT, MAY** in uppercase to signal normative scope. IETF RFC 2119 convention, universal in spec writing.

- **MUST / MUST NOT** — absolute obligation; deviation forbidden.
- **SHOULD / SHOULD NOT** — strong recommendation; justifiable deviation must be flagged.
- **MAY** — option, freely chosen depending on context.

Example: *"Every agent markdown file MUST carry a conformant YAML frontmatter. The `version` field SHOULD be present for reference sheets. A `module-readme` file MAY reference relative paths."*

## Application by artifact type

| Type | Typical H2 sections | Required frontmatter | Optional |
|---|---|---|---|
| `agent-instructions` (root/module CLAUDE.md) | Context, Doctrine, Pointers, Boundaries | `name`, `description`, `type` | `module` |
| `skill` (SKILL.md) | Context, Doctrine, Pointers, Boundaries, Examples | `name`, `description`, `type` | — |
| `adr` | Context, Options considered, Decision, Consequences | `name`, `description`, `type` | — |
| `reference` | (free, follow external material) | `name`, `description`, `type`, `version` | — |
| `research` | (free, follow investigation material) | `name`, `description`, `type`, `date` | — |
| `module-readme` (module README.md) | Context, Pointers | `name`, `description`, `type`, `module` | — |

ADRs have specific sections (`Context`, `Options considered`, `Decision`, `Consequences`) that fall outside the general closed vocabulary — an accepted exception, inherited from the Nygard format.
