# ADR-0001 — Adopter le pattern Architecture Decision Records

**Status** — accepted le 2026-05-22

## Contexte
Deckle accumule des décisions structurantes — choix de packaging, de distribution, de patterns d'app lifecycle, de matériel cible, de plateforme native — qui ont été tranchées au fil des sessions mais jamais cristallisées dans un endroit canonique du repo. La mémoire d'agent et les commits portent l'historique, ce qui rend la consultation indirecte et la révision difficile. Quand une décision est ré-évaluée, on perd la trace de ce qui avait été pesé contre quoi à l'époque.

Le skill `deckle-docs` prévoyait initialement un journal des décisions par module, mais cette architecture diluait la trace décisionnelle et créait une frontière floue avec les `CLAUDE.md` de module (eux-mêmes documents vivants, mal adaptés à de l'historique immuable).

## Options considérées
- **A. Continuer en mémoire d'agent et commits** — pas de friction d'écriture, mais coût élevé de retrouvabilité et de relecture pour un nouveau contributeur ou pour le maintainer six mois plus tard. Aucune trace publique consultable depuis le repo.
- **B. Journal de décisions par module dans `CLAUDE.md`** — accessible mais brouille le contrat « document vivant édité en continu ». Le journal devient une zone en append qui pollue la doctrine du module.
- **C. Adopter le pattern Architecture Decision Records ([Nygard 2011](https://www.cognitect.com/blog/2011/11/15/documenting-architecture-decisions))** dans `docs/adr/` — fichiers immuables, numérotés séquentiellement, un par décision. Format léger, immutabilité par construction, références cross-ADR explicites. Convention de nommage héritée d'[adr-tools](https://github.com/npryce/adr-tools).

## Décision
Option C. Adoption du pattern ADR pour toutes les décisions structurantes — cross-modules, structure de repo, choix de distribution, choix de plateforme, conventions transverses, dépendances de fond. Format MADR-minimal sans frontmatter YAML — `Status` en première ligne du corps, sections en prose libre. Nommage `NNNN-titre-kebab.md`, quatre chiffres, jamais réutilisé. Le template canonique et la doctrine de placement vivent dans le skill `deckle-docs`.

## Conséquences
Toute nouvelle décision structurante crée un ADR avant ou immédiatement après son application en code. Les décisions déjà tracées en mémoire sont promues en ADR rétroactifs au fil de l'eau, pas en passe globale. Une décision révisée crée un nouvel ADR ; l'ancien passe en `superseded` avec lien explicite vers le successeur, son corps reste lisible tel qu'il avait été écrit. Le `CLAUDE.md` racine et de module peut référencer un ADR sans recopier son contenu. Le journal de décisions par module prévu dans la version précédente du skill `deckle-docs` est supprimé — la trace décisionnelle vit exclusivement ici.
