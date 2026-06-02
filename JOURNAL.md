---
name: journal-deckle
description: "Journal daté du projet Deckle : avancées techniques validées, observations en cours marquées comme hypothèses, retours d'usage, apprentissages méthodologiques. Complément réversible aux ADRs (qui figent les décisions stables) et aux CLAUDE.md (qui portent la doctrine timeless)."
type: project-journal
---

# Journal — Deckle

## Pourquoi ce fichier

Les **ADRs** (`docs/adr/NNNN-*.md`) actent les décisions **stables** — une fois mergées, elles sont figées, et une révision crée un nouvel ADR qui supersede. C'est cher à produire, c'est définitif. Les **`CLAUDE.md`** par module portent la doctrine **timeless** — règles durables qui se mettent à jour en place, sans datation.

Entre les deux il reste beaucoup de matière qui mérite d'être tracée mais ne tient ni dans un ADR (pas assez tranché, pas frozen) ni dans un `CLAUDE.md` (datée, conjoncturelle). Une avancée technique livrée et validée, une observation en cours marquée comme hypothèse, un jalon transverse, un apprentissage méthodo, un retour d'usage. Ces choses méritent d'être notées **datées**, mais sans le poids cérémoniel d'un ADR.

Le journal accueille ça. Format : entrées chronologiques en H2 daté `YYYY-MM-DD`, titre court, corps prose. **Réversibilité assumée** — on peut éditer, refondre, archiver une entrée vieillie, au contraire des ADRs. Si une entrée stabilise en décision durable, elle est promue en ADR à ce moment-là ; si elle stabilise en règle timeless, elle est lifted vers le `CLAUDE.md` concerné. Le journal devient alors un pointeur.

Les entrées récentes sont en haut. À chaque nouvelle, ajouter au sommet, pas à la fin.

La doctrine complète — granularité d'une entrée, périmètre admis, rigueur épistémique, articulation avec les artefacts voisins — vit dans le skill [`deckle-journal`](.claude/skills/deckle-journal/SKILL.md).

---

## 2026-05-27 — Refonte format des artefacts agents

Adoption du format canonique unifié pour tous les artefacts agents — `CLAUDE.md` racine et par module, `SKILL.md` sous `.claude/skills/`, ADRs, sheets `reference` et `research`, READMEs de module. Le frontmatter YAML devient obligatoire (`name`, `description`, `type`), le vocabulaire d'H2 est fermé (Role, Context, Doctrine, Pointers, Boundaries, Examples), la convention RFC 2119 (MUST / SHOULD / MAY) cadre les paragraphes prescriptifs.

**Livré** dans le merge `docs/refonte-format-artefacts-agents` ([c58a303](https://github.com/) — `merge: docs/refonte-format-artefacts-agents — ADR-0013 et migration des artefacts au format canonique`). Migration complète des artefacts existants livrée dans la branche avant merge : tous les `CLAUDE.md` modulaires, tous les `SKILL.md` projet, les sheets `docs/reference/` et `docs/research/`, le `scripts/README.md`, les artefacts `benchmark/`.

**Référence normative** : `session-save-context/format.md` (skill global sous `~/.claude/skills/session-save-context/`) pour le format technique réutilisable par d'autres skills.

**Conséquence opérationnelle** : tout nouvel artefact agent créé désormais doit passer par le format canonique. Le hook `scripts/hooks/update-tree.ps1` scrape déjà `name` + `description` + `type` pour les afficher dans `TREE.md` — la conformité du frontmatter conditionne la lisibilité de l'arborescence.

**Apprentissage méthodo** — la migration a révélé deux artefacts en dérive silencieuse par rapport à la liste fermée des `type`. Le journal benchmark écrit organiquement avec `type: module-journal` n'était pas reconnu par `format.md`, et il manquait `project-journal` pour ce fichier-ci. Les deux types ont été promus dans la liste fermée à l'occasion de l'introduction du JOURNAL projet (2026-05-27). La règle : quand une convention organique tient au moins une fois sur le terrain, elle est promue dans le format avant qu'un agent en aval ne s'égare.
