---
name: deckle-docs
description: Convention documentaire pour le projet Deckle (Windows .NET 10 / WinUI 3). Définit où chaque trace écrite vit — un fichier de description externe et un fichier d'instructions pour agent LLM par module, un journal des décisions centralisé en ADR à la racine sous docs/adr/, des fiches de référence stables versionnées sous docs/reference/, des notes de recherche datées sous docs/research/, des références importées d'autres LLMs sous docs/references/external/. Porte également la doctrine des commentaires en code (pourquoi plutôt que quoi, vérité actuelle vérifiée, discipline des marqueurs). Réconcilie la nomenclature documentaire universelle de personal-conventions avec un placement projet-local. Triggers on phrases like deckle docs, deckle documentation, convention doc deckle, ADR deckle, journal décisions deckle, instructions agent module deckle, où je mets cette doc deckle, références externes deckle, commentaires code deckle, hygiène commentaires deckle.
---

# Deckle — Convention documentaire et commentaires

## Rôle

Skill projet-spécifique qui répond à deux questions : **où une information écrite vit-elle dans Deckle**, et **comment elle est rédigée pour rester vraie et utile**. S'invoque avant d'ajouter une fiche, de créer un ADR, de migrer une référence existante, de nettoyer des commentaires obsolètes, ou de promouvoir une décision en trace historique.

Complète `personal-conventions` qui porte la nomenclature universelle des documents stables exportés. `personal-conventions` dit *comment nommer* un document stable destiné à être archivé ou partagé hors du projet. `deckle-docs` dit *où il vit dans Deckle et quand on en crée un*. C'est le complément projet-local qui manquait et qui avait laissé la dérive documentaire s'installer.

## Architecture documentaire en quatre régimes

**Documents vivants** — `CLAUDE.md` racine et de chaque module. Mis à jour en continu, jamais versionnés dans le nom. Portent la doctrine atemporelle, les conventions internes, les pièges, les anti-patterns. Quand une règle change, on édite le fichier.

**Journal des décisions** — `docs/adr/NNNN-titre-kebab.md`. Architecture Decision Records au format Nygard minimal, immuables une fois mergés, numérotés séquentiellement. Un ADR capture une décision tranchée à un instant T : contexte, alternatives considérées, décision, conséquences. Une décision révisée crée un nouvel ADR qui supersède l'ancien — pas de réécriture en place.

**Documents stables versionnés** — `docs/reference/préfixe--slug--V.V.md`. Specs de sous-système, inventaires normatifs, recettes techniques externes, audits stabilisés. Versionnés (V majeur = refonte de fond, V mineur = évolution incrémentale). Mis à jour rarement, par révision substantielle.

**Matériau de pensée non-doctrinal** — deux zones distinctes. `docs/research/research--slug--YYYY-MM-DD.md` reçoit les notes de recherche internes datées (typiquement produites par un sous-agent en amont d'un jalon). `docs/references/external/external--sujet--source-YYYY-MM-DD.md` reçoit le matériau importé d'autres LLMs ou de sources tierces. Première ligne du fichier en blockquote `> Source : ChatGPT GPT-5 · 2026-05-15 · prompt original archivé en fin de fichier`. Ces fichiers ne sont jamais des sources de vérité — ils sont des artefacts qu'on consulte, qu'on cite éventuellement depuis un ADR ou une reference, et qu'on garde tels quels.

## Deux fichiers canoniques par module

Chaque module peut porter deux fichiers à sa racine. Pas obligatoires — un module trivial peut n'en avoir aucun. Mais ces deux rôles sont les seuls autorisés pour de la doc module-locale.

**Description externe.** Ce que fait le module, sa responsabilité, ses dépendances vers les autres modules, comment l'utiliser depuis l'app hôte. Court, lisible en deux minutes, destiné à un contributeur humain qui découvre le module. Pas d'historique, pas de doctrine interne, pas de pièges techniques détaillés.

**Instructions agent.** Doctrine atemporelle du module, conventions internes, pièges connus, anti-patterns à refuser, exemples canoniques. C'est ce qu'un agent LLM doit lire avant de modifier le module. Prose dense, paragraphes courts, pas de listes inutiles. La doctrine y vit en permanence — si une règle change, ce fichier est mis à jour. Nom standardisé que l'écosystème d'agents converge à reconnaître, indépendant du fournisseur de LLM.

Aucun journal de décisions par module. La trace décisionnelle vit exclusivement en ADR à la racine.

## Frontière ADR vs `CLAUDE.md` de module

Une décision crée un ADR si **au moins l'une** de ces conditions est vraie. Elle touche plusieurs modules. Elle touche la structure du repo ou du build. Elle touche le contrat externe — clavier, autostart, paths, distribution, plateforme cible. Elle a écarté des alternatives qu'on pourrait re-considérer plus tard. Elle pose un revers (`accepting that...`) qu'on devra ré-évaluer. En cas de doute, ADR.

Les détails purement internes d'un module (pattern d'appel d'une lib, piège local, anti-pattern récurrent) restent dans le `CLAUDE.md` du module — ce sont des règles vivantes, pas des décisions à graver.

Un `CLAUDE.md` peut référencer un ADR par lien explicite (`Voir ADR-0002 sur MSIX`) sans recopier son contenu. Pas de duplication doctrinale.

Quand une décision documentée dans un `CLAUDE.md` se révèle en pratique avoir des conséquences cross-modules, la promotion en ADR est un acte explicite — création de l'ADR, mention dans le `CLAUDE.md` concerné, lien retour. Pas de promotion silencieuse.

## Format ADR

Pas de frontmatter YAML. L'identité est portée par le nom de fichier et redondée dans le titre H1. Le statut, la date et les liens supersedes vivent en prose lisible humainement.

Template canonique à utiliser pour chaque nouvel ADR. Le numéro suit la séquence du dossier (regarder le plus grand `NNNN` existant et incrémenter), jamais réutilisé même après superseded.

```markdown
# ADR-NNNN — Titre court de la décision

**Status** — accepted le YYYY-MM-DD

## Contexte
La situation qui rend la décision nécessaire. Forces en présence,
contraintes, déclencheur de la réflexion.

## Options considérées
- **A. Première option** — caractéristique principale, ce qu'elle
  exige, ce qu'elle apporte, ce qu'elle coûte.
- **B. Deuxième option** — idem.
- **C. Troisième option** — idem.

## Décision
La voie retenue, en une à trois phrases. Voix active.

## Conséquences
Ce qui devient plus facile, plus difficile, ou impossible à partir
de cette décision. Conditions de ré-évaluation s'il y en a.
```

Le `Status` évolue dans le temps avec quatre valeurs possibles : `proposed`, `accepted`, `superseded`, `deprecated`. Quand un ADR supersède un autre, on édite la ligne `Status` de l'ancien pour passer à `superseded le YYYY-MM-DD par [ADR-NNNN](./NNNN-titre.md)`. Le contenu du corps reste inchangé — la valeur historique d'un ADR superseded est précisément de garder lisible ce qui avait été pesé à l'époque.

Le premier ADR du dossier est `0001-record-architecture-decisions.md`, qui acte rétrospectivement l'usage des ADR. Convention héritée d'`adr-tools` (Nat Pryce) qu'on adopte sans utiliser l'outil lui-même (cassé sous Windows).

## Périmètre projet vs nomenclature universelle

La nomenclature universelle de `personal-conventions` s'applique aux **documents stables exportés** : releases versionnées, specs cross-projet, recettes techniques de référence, rapports archivés. Ces documents sont versionnés, datés, lus dans un état figé. Ils peuplent `docs/reference/`.

Les `CLAUDE.md` sont des **documents vivants** — pas de version dans le nom, mis à jour en continu, lus dans leur état présent. Ils suivent leur convention de nommage propre (majuscules consacrées par l'écosystème open source) et n'entrent pas dans la nomenclature universelle.

Les ADR sont des **documents immuables datés** — pas de version dans le nom non plus, mais pas vivants pour autant. L'immuabilité prend en charge ce que le versionnage prenait en charge ailleurs. Une décision révisée n'écrase pas l'ancienne, elle la supersède.

## Doctrine sur les commentaires en code

Les agents LLM lisent les commentaires comme s'ils étaient vrais. Un commentaire faux ou obsolète **est plus nocif** qu'une absence de commentaire — il pollue le raisonnement à chaque lecture du fichier. Quatre règles dures.

**Pourquoi, pas quoi.** Le code dit déjà le quoi. Un commentaire qui paraphrase le nom de la fonction ou la valeur d'une constante est du bruit. Un commentaire qui explique *pourquoi* une décision a été prise mérite d'exister — typiquement parce que la décision est contre-intuitive ou non locale. Si le pourquoi tient en une phrase, commentaire au-dessus. Si le pourquoi mérite un paragraphe, c'est un ADR avec un pointeur depuis le commentaire (`// Voir ADR-0004 sur le lazy windows`).

**Vérité actuelle.** Un commentaire qui était vrai à une époque mais qui ne l'est plus aujourd'hui doit être supprimé ou corrigé. En écriture courante, quand on touche du code dont les commentaires sont alentour, vérifier qu'ils sont encore exacts — sinon mettre à jour ou supprimer.

**Discipline des marqueurs.** Un marqueur de dette (TODO, HACK, FIXME) sans contexte devient un fossile. Soit on l'enlève en réalisant le travail ou en décidant qu'il n'est plus pertinent, soit on lui met un format minimal qui le rend traçable. Un marqueur de dette assumée mérite un ADR ; le commentaire pointe vers l'ADR.

**Préférer le nom.** Un commentaire explicatif est souvent le symptôme d'un nom mal choisi ou d'une fonction trop longue. Avant d'écrire un commentaire, se demander si renommer une variable ou extraire une sous-fonction réglerait le besoin. Le code se lit de mieux en mieux ; le commentaire reste figé.

## Hygiène par module, pas en passe géante

La passe de nettoyage des commentaires se fait **module par module au moment où on travaille ce module**, jamais en passe géante centralisée sur tout le dépôt. Quand un module entre en refonte, on lance un agent ciblé qui parcourt ses fichiers, scanne les commentaires significatifs, confronte chaque assertion au code voisin actuel, signale ce qui est obsolète ou potentiellement faux. L'agent propose un patch et une liste de fragments à promouvoir en ADR parce qu'ils racontent un pourquoi qui mérite trace historique. La promotion est un acte délibéré qui structure l'historique du projet, pas un dépotoir.

## Pointeurs

- **`personal-conventions`** — nomenclature universelle des documents stables exportés, conventions cross-projet (langue, wording, git, worktrees). `deckle-docs` complète, ne remplace pas.
- **`deckle-refonte`** — skill orchestrateur qui pointe vers ce skill quand un chantier touche au volet documentation.
