---
name: adr-0013-format-canonique-artefacts-agents
description: "Acte le format normatif des artefacts agents Deckle : langue anglaise par défaut, frontmatter YAML obligatoire, vocabulaire fermé de sections H2, marqueurs normatifs RFC 2119. Révise la clause 'sans frontmatter YAML' de l'ADR-0001."
type: adr
---

# ADR-0013 — Format canonique des artefacts agents

**Status** — accepted le 2026-05-25

## Contexte

Les artefacts agents du projet — `CLAUDE.md` racine et par module, fichiers `SKILL.md` sous `.claude/skills/`, ADR sous `docs/adr/`, fiches sous `docs/reference/` et `docs/research/`, prompts de spawn-task, README de module — se sont accumulés sans format uniforme. Trois frottements concrets ont émergé.

Le contenu projet a été rédigé en français pour suivre la langue de travail de Louis, mais les artefacts agents sont lus par des agents qui opèrent mieux sur prompts anglais et qui croisent les artefacts entre projets. La langue mixte coûte à chaque lecture.

Sans frontmatter scannable, aucun outil tiers ne peut extraire le sens d'un fichier pour l'afficher en index. Le script `scripts/update-tree.ps1` ne pouvait que lister les chemins sans contextualiser. Une fonction comme « lister tous les ADR concernant l'observabilité » nécessitait de grep le contenu plein, lent et fragile.

Chaque `CLAUDE.md`, chaque `SKILL.md`, chaque fiche reference réinventait sa structure H2. La lecture transverse était fragmentée — un agent qui cherche « les règles dures du module X » devait deviner où chaque artefact range cette information.

L'[ADR-0001](0001-record-architecture-decisions.md) du 2026-05-22 prescrivait explicitement « MADR-minimal sans frontmatter YAML ». Cette clause était cohérente avec la doctrine documentaire de l'époque (zéro friction d'écriture, format ultra-léger), mais elle devient un obstacle dès qu'on veut outiller la lecture des artefacts.

La discussion en session du 2026-05-25 a abouti à un format canonique unifié, ancré dans la fiche normative [`.claude/skills/session-save-context/format.md`](../../.claude/skills/session-save-context/format.md). La création d'un fichier `AGENTS.md` global au sens de la spec OpenAI/Anthropic — qui aurait centralisé un index lisible par tout agent — est différée jusqu'à un support natif de la spec par Claude Code.

## Options considérées

- **A. Laisser chaque artefact libre.** Zéro friction d'écriture, mais le frottement de lecture continue à s'accumuler. Aucun outillage générique possible. Statu quo qui s'aggrave.
- **B. Frontmatter optionnel, vocabulaire libre, langue libre.** Demi-mesure qui ne résout aucun des trois frottements. Un frontmatter inconsistant est pire que pas de frontmatter du tout pour le scraping — l'outillage doit gérer deux cas.
- **C. Format canonique imposé.** Anglais par défaut, frontmatter YAML obligatoire avec champs définis par type d'artefact, vocabulaire fermé de sections H2, RFC 2119 dans les paragraphes prescriptifs. Coût initial de mise à niveau des artefacts existants, mais bénéfice composant durable.

## Décision

Option C. Quatre clauses imbriquées, normative reference unique : [`.claude/skills/session-save-context/format.md`](../../.claude/skills/session-save-context/format.md).

1. **Langue.** Les artefacts agents sont rédigés en anglais — `CLAUDE.md` racine et module, `SKILL.md`, ADR, fiches reference et research, README de module. Exception : les transcriptions verbatim de Louis (prose conversationnelle citée dans un skill, dans une fiche research, dans un ADR) restent en français et ne sont pas traduites. Une transcription en français au sein d'un artefact en anglais n'altère pas la langue dominante.

2. **Frontmatter YAML obligatoire.** Tout artefact agent porte en tête un frontmatter entre `---` markers avec trois champs requis : `name` (slug kebab-case unique dans sa catégorie), `description` (une ligne — *what* le doc dit + *when* le lire ou l'invoquer), `type` (un de `agent-instructions`, `skill`, `adr`, `reference`, `research`, `module-readme`). Champs optionnels selon `type` : `module` (pour les `agent-instructions` module-scoped), `version` (pour les `reference` versionnées), `date` (pour les `research` datées). Pour les skills, la `description` inclut en plus les phrases trigger plausibles.

3. **Vocabulaire fermé de sections H2.** Quand une H2 apparaît dans un artefact agent, elle porte un nom canonique parmi `Role`, `Context`, `Doctrine`, `Pointers`, `Boundaries`, `Examples`. Pas de skeleton imposé — chaque artefact instancie les sections dont il a besoin et omet les autres — mais une H2 instanciée respecte le vocabulaire. Les ADR conservent leur structure Nygard (`Contexte`, `Options considérées`, `Décision`, `Conséquences`) comme exception assumée héritée du format historique.

4. **Marqueurs normatifs RFC 2119.** Dans les paragraphes prescriptifs, utiliser `MUST`, `MUST NOT`, `SHOULD`, `SHOULD NOT`, `MAY` en uppercase pour signaler la portée normative — obligation absolue, recommandation forte, option ouverte. Convention IETF universelle dans l'écriture de spec, transparente pour qui la lit.

## Conséquences

La clause « MADR-minimal sans frontmatter YAML » de l'[ADR-0001](0001-record-architecture-decisions.md) est révisée — les ADR portent désormais un frontmatter conforme à la clause 2. Le reste de l'ADR-0001 (pattern Nygard, nommage `NNNN-titre-kebab.md`, immutabilité, numérotation jamais réutilisée) reste valide. Le présent ADR est lui-même le premier à porter un frontmatter, appliquant sa propre décision dès sa rédaction.

Les artefacts existants — `CLAUDE.md` racine plus quatorze modules, `CONTEXT.md`, neuf skills `deckle-*`, douze ADR, fiches research — seront migrés progressivement vers le format canonique. Pas de big-bang. Chaque artefact touché en cours de session bascule au format à l'occasion. Une passe coordonnée de traduction et de mise au format est suivie en mémoire roadmap comme chantier dédié.

La skill `session-save-context` impose le frontmatter à toute écriture nouvelle ou modification substantielle qu'elle effectue. Ses boundaries listent « Verify the frontmatter before writing » comme contrôle obligatoire.

Le script `scripts/update-tree.ps1` scrape le frontmatter et affiche `name` + `description` + `type` à côté de chaque markdown dans `TREE.md`. La lecture transverse du repo devient navigable sans grep.

La création d'un fichier `AGENTS.md` global est différée jusqu'à un support natif par Claude Code — un `AGENTS.md` non lu par l'outil reste théorique. Le format canonique des artefacts agents est suffisant pour rendre le repo lisible aux agents pour le moment.
