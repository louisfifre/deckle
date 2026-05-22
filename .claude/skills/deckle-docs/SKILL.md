---
name: deckle-docs
description: Convention documentaire pour le projet Deckle (Windows .NET 10 / WinUI 3). Définit où chaque trace écrite vit — un fichier de description externe, un fichier d'instructions pour agent LLM, un fichier de journal des décisions — par module plus une racine minimale pour le véritablement transverse. Porte également la doctrine des commentaires en code (pourquoi plutôt que quoi, vérité actuelle vérifiée, discipline des marqueurs, promotion vers journal). Réconcilie la nomenclature documentaire universelle de personal-conventions avec un placement projet-local. Triggers on phrases like deckle docs, deckle documentation, convention doc deckle, journal module deckle, instructions agent module deckle, où je mets cette doc deckle, commentaires code deckle, hygiène commentaires deckle.
---

# Deckle — Convention documentaire et commentaires

## Rôle

Skill projet-spécifique qui répond à deux questions : **où une information écrite vit-elle dans Deckle**, et **comment elle est rédigée pour rester vraie et utile**. S'invoque avant d'ajouter une fiche, de migrer une référence existante, de nettoyer des commentaires obsolètes, ou de promouvoir une décision en trace historique.

Complète `personal-conventions` qui porte la nomenclature universelle des documents stables exportés. `personal-conventions` dit *comment nommer* un document stable destiné à être archivé ou partagé hors du projet. `deckle-docs` dit *où il vit dans Deckle et quand on en crée un*. C'est le complément projet-local qui manquait et qui a permis la dérive documentaire historique vers une racine `docs/` fourre-tout.

## Trois fichiers canoniques par module

Chaque module peut porter trois fichiers à sa racine, chacun avec un rôle précis et une durée de vie distincte. Pas tous obligatoires — un module trivial peut n'avoir qu'un seul. Mais ces trois rôles sont les seuls autorisés pour de la doc module-locale.

**Description externe.** Ce que fait le module, sa responsabilité, ses dépendances vers les autres modules, comment l'utiliser depuis l'app hôte. Court, lisible en deux minutes, destiné à un contributeur humain qui découvre le module. Pas d'historique, pas de doctrine interne, pas de pièges techniques détaillés.

**Instructions agent.** Doctrine atemporelle du module, conventions internes, pièges connus, anti-patterns à refuser, exemples canoniques. C'est ce qu'un agent LLM doit lire avant de modifier le module. Prose dense, paragraphes courts, pas de listes inutiles. La doctrine y vit en permanence — si une règle change, ce fichier est mis à jour et le changement est tracé dans le journal du module. Nom standardisé que l'écosystème d'agents converge à reconnaître, indépendant du fournisseur de LLM.

**Journal des décisions.** Historique des décisions, allers-retours, voies explorées et abandonnées, retours d'expérience. Une entrée par décision ou chantier significatif, datée. Sections récurrentes par entrée : contexte (ce qu'on cherchait), voies évaluées (avec verdict), décision (ce qui a été retenu et pourquoi), conséquences. Les entrées ne sont jamais réécrites — si une décision passée est révisée, on crée une nouvelle entrée qui pointe vers l'ancienne. C'est ce que Louis appelle « l'historique de chacun des modules, garder pourquoi on n'est pas allé sur telle techno, qu'est-ce qu'on a évalué ».

## Trois fichiers à la racine du dépôt

Le même trio plus une nuance. La description racine présente le projet entier pour un contributeur extérieur. Les instructions agent racine portent la doctrine cross-projet et les règles non négociables. Le journal racine garde l'historique des décisions transverses qui touchent plusieurs modules ou la structure du dépôt.

Quand une décision touche un seul module, elle va dans le journal de ce module. Quand elle touche plusieurs modules ou la structure du dépôt, elle va dans le journal racine. Si on hésite, c'est probablement racine — un journal de module doit rester focal.

## Ce qui reste à la racine `docs/`

Le dossier `docs/` racine ne disparaît pas mais se réduit drastiquement à ce qui est véritablement transverse et ne tient pas dans un journal.

**Recettes techniques externes** — comment recompiler une dépendance native, comment provisionner l'environnement de dev, comment exécuter une procédure de récupération. Documents stables qui suivent la nomenclature universelle de `personal-conventions`.

**Audits stabilisés** — rapports de revue (sécurité, performance, accessibilité) qui ont une date et un verdict, à conserver pour mémoire.

**Archives** — recherches datées, fiches obsolètes qu'on veut garder pour mémoire historique sans encombrer le chemin actif. Sous un sous-dossier dédié.

Rien d'autre. Aucune fiche de référence par module — ces fiches vivent dans les instructions agent du module concerné.

## Périmètre projet vs nomenclature universelle

La nomenclature universelle de `personal-conventions` s'applique aux **documents stables exportés** : releases versionnées, specs cross-projet, recettes techniques de référence, rapports archivés. Ces documents sont versionnés, datés, et ont une vocation à être lus dans un état figé.

Les fichiers canoniques module-locaux sont des **documents vivants** — pas de version dans le nom, mis à jour en continu, lus dans leur état présent. Ils suivent leur propre convention de nommage stable (les majuscules consacrées par l'écosystème open source) et n'entrent pas dans la nomenclature universelle.

Cette distinction règle l'ambiguïté qui avait laissé la dérive documentaire s'installer : tout ce qui semble « important » n'est pas pour autant un document stable exporté. La doctrine vivante d'un module relève des documents vivants, pas de la nomenclature versionnée.

## Doctrine sur les commentaires en code

Les agents LLM lisent les commentaires comme s'ils étaient vrais. Un commentaire faux ou obsolète **est plus nocif** qu'une absence de commentaire — il pollue le raisonnement à chaque lecture du fichier. Quatre règles dures.

**Pourquoi, pas quoi.** Le code dit déjà le quoi. Un commentaire qui paraphrase le nom de la fonction ou la valeur d'une constante est du bruit. Un commentaire qui explique *pourquoi* une décision a été prise mérite d'exister — typiquement parce que la décision est contre-intuitive ou non locale. Si le pourquoi tient en une phrase, commentaire au-dessus. Si le pourquoi mérite un paragraphe, c'est une entrée dans le journal du module avec un pointeur depuis le commentaire.

**Vérité actuelle.** Un commentaire qui était vrai à une époque mais qui ne l'est plus aujourd'hui doit être supprimé ou corrigé. En écriture courante, quand on touche du code dont les commentaires sont alentour, vérifier qu'ils sont encore exacts — sinon mettre à jour ou supprimer.

**Discipline des marqueurs.** Un marqueur de dette (TODO, HACK, FIXME) sans contexte devient un fossile. Soit on l'enlève en réalisant le travail ou en décidant qu'il n'est plus pertinent, soit on lui met un format minimal qui le rend traçable. Un marqueur de dette assumée mérite une entrée dans le journal du module ; le commentaire pointe vers l'entrée.

**Préférer le nom.** Un commentaire explicatif est souvent le symptôme d'un nom mal choisi ou d'une fonction trop longue. Avant d'écrire un commentaire, se demander si renommer une variable ou extraire une sous-fonction réglerait le besoin. Le code se lit de mieux en mieux ; le commentaire reste figé.

## Hygiène par module, pas en passe géante

La passe de nettoyage des commentaires se fait **module par module au moment où on travaille ce module**, jamais en passe géante centralisée sur tout le dépôt. Quand un module entre en refonte, on lance un agent ciblé qui parcourt ses fichiers, scanne les commentaires significatifs, confronte chaque assertion au code voisin actuel, signale ce qui est obsolète ou potentiellement faux. L'agent propose un patch et une liste de fragments à promouvoir vers le journal du module parce qu'ils racontent un pourquoi qui mérite trace historique. La promotion est un acte délibéré qui structure l'historique du module, pas un dépotoir.

## Migration des fichiers d'instructions agent historiques

Acte symbolique de la convention. Les fichiers d'instructions agent historiques portent un nom hérité du fournisseur de LLM ; ils sont renommés vers le nom standardisé indépendant du fournisseur. Le contenu reste, le nom change. C'est l'occasion d'une relecture pour alléger ce qui aurait dérivé.

## Pointeurs

- **`personal-conventions`** — nomenclature universelle des documents stables exportés, conventions cross-projet (langue, wording, git, worktrees). `deckle-docs` complète, ne remplace pas.
- **`deckle-refonte`** — skill orchestrateur qui pointe vers ce skill quand un chantier touche au volet documentation.
