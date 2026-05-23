---
name: deckle-commits
description: Doctrine de commits pour le projet Deckle (Windows .NET 10 / WinUI 3). Porte le format Conventional Commits adapté, le vocabulaire fermé des types et des scopes alignés sur les modules, la doctrine de grain qui impose une intention sémantique unique par commit, le traitement des refontes cross-module, la convention des merge commits de feature branch, et la règle d'identité auteur qui exclut tout trailer de co-signature LLM. S'invoque avant chaque acte de commit, avant la définition d'une stratégie de séquençage pour un gros chantier, et lors d'un audit d'historique. Triggers on phrases like commit deckle, message commit deckle, conventional commits deckle, scope commit deckle, grain commit deckle, splitter commit deckle, mégacommit deckle, refactor transverse commit deckle, merge commit deckle, identité auteur commit deckle, audit historique deckle, Co-Authored-By Claude deckle.
---

# Deckle — Doctrine de commits

## Rôle

Skill projet-spécifique qui répond à une question récurrente : **quel commit, avec quel message, à quel grain**. S'invoque avant chaque `git commit` non trivial, au moment de séquencer un gros chantier en commits intermédiaires, et quand on audite l'historique d'une branche ou du dépôt.

Complète deux ressources distinctes. `git-commit` (skill global) porte la **mécanique** d'exécution — analyser le diff, stager, formuler, exécuter. `personal-conventions` porte les **règles cross-projet** — langue, conventions de branche, worktrees. `deckle-commits` est la couche projet-locale qui code la doctrine appliquée par le moteur et les choix Deckle-spécifiques (vocabulaire des scopes, granularité attendue, identité auteur).

## Posture sémantique

Un commit représente **une intention claire et autonome**. Ni « tout ce qui a été fait dans la journée », ni « tout ce qui touche un module ». Trois propriétés en découlent qui sont les bénéfices recherchés et le test de la doctrine. **Bisectabilité** — `git bisect` doit pouvoir isoler la cause d'un bug à un commit précis ; un commit qui mélange deux changements détruit cette propriété. **Lisibilité historique** — la lecture séquentielle du `git log` raconte une progression intelligible ; un mégacommit fond ce récit en bouillie. **Réversibilité ciblée** — un `git revert` doit pouvoir défaire une étape sans casser le reste ; deux intentions fondues forcent un revert tout-ou-rien.

La règle inverse vaut aussi : un commit qui ne fait que la moitié d'un changement, laissant le repo dans un état incohérent, n'est pas atomique non plus. L'atomicité est l'unité sémantique minimale qui laisse le code dans un état qui compile et tient debout.

## Format adopté

Conventional Commits v1.0.0 (voir [conventionalcommits.org](https://www.conventionalcommits.org/en/v1.0.0/)). La forme canonique est `type(scope): description`, sujet en impératif présent, première lettre en minuscule, sans point final. Cible de longueur **72 caractères pour le sujet**, qui est la longueur lisible dans `git log --oneline` et les UI GitHub sans troncature ; la règle stricte 50/72 héritée de Tim Pope est un idéal — Deckle l'allège à 72 pour le sujet parce que la combinaison `type(scope):` consomme déjà des caractères et que la lisibilité du sujet brut prime sur l'idéal de concision.

Le body optionnel est séparé du sujet par une **ligne vide**, wrap à 72 caractères, formulé pour dire **pourquoi** le changement existe — pas ce que le diff montre déjà. Les footers vivent après une ligne vide finale et portent les références traçables : `refs ADR-NNNN` quand le commit acte une décision documentée en ADR, `refs #123` pour un ticket, `BREAKING CHANGE: …` pour un changement de contrat externe (Deckle n'a pas encore de release publique consommée, donc cette mention sert surtout à signaler ce qui devra apparaître au moment d'une 1.0).

## Vocabulaire fermé des types

Onze types admis, alignés sur le standard. **`feat`** introduit une fonctionnalité ou un comportement utilisateur nouveau. **`fix`** corrige un bug. **`refactor`** change la structure interne sans modifier le comportement observable. **`docs`** touche exclusivement la documentation. **`test`** ajoute ou modifie des tests. **`perf`** améliore une performance mesurable. **`style`** corrige du formatage sans logique. **`build`** modifie le système de build, les dépendances, les scripts de packaging. **`ci`** modifie l'intégration continue (Deckle n'en a pas encore, mais le type reste réservé). **`chore`** est le réceptacle des maintenances qui ne rentrent pas ailleurs (`.gitignore`, fichiers de config, housekeeping). **`revert`** défait un commit antérieur.

Un type local conservé : **`merge`** pour les commits de merge de feature branch dans `main`, dans la forme `merge: <branch-name> — <résumé court>`. Conserve la lisibilité des merges à plat dans `git log --oneline`. C'est une dérogation assumée à la spec Conventional Commits, justifiée par le workflow projet (`--no-ff` sur les feature branches).

**Types à proscrire** parce qu'ils sont apparus ad hoc et fragmentent le vocabulaire : `prep`, `tune`, `tools`, `bench`, `tweak`, `hud`, `settings`, `engine`, `logs`. Ces intentions tombent toutes dans `feat`, `refactor`, `chore` ou `docs`. Pour les itérations de benchmark, le bon format est `chore(bench): iteration N — …` — le scope porte le contexte, pas le type.

## Vocabulaire fermé des scopes

Le scope reflète la **frontière touchée**, pas l'auteur ni l'environnement. Pour Deckle, il miroite la liste des modules canoniques — `core`, `audio`, `vision`, `lighting`, `ambient`, `chrono`, `composition`, `catalog`, `shell`, `settings`, `whisp`, `llm`, `playground`, `hud` (au sens `Deckle.Chrono.Hud`). Trois scopes transverses sont admis quand le commit ne touche pas un module particulier mais une frontière du projet : **`scripts`** pour `scripts/`, **`docs`** pour `docs/` à la racine (et n'apparaît qu'en redondance avec le type `docs:` quand on veut désambiguïser une page précise), **`agent`** pour les `CLAUDE.md` et les skills sous `.claude/`.

**Un seul scope par commit.** La forme virgulée `feat(playground, ambient): …` qui est apparue dans l'historique est un signal de découpage : soit le commit mêle deux intentions et il faut le splitter, soit le scope réel est un thème cross-module (`refactor(observability)`, `refactor(catalog)`) qu'il faut nommer. Si un scope thématique transverse se met à apparaître à répétition, c'est un signal de promotion en sous-namespace dédié — voir `deckle-modularite`.

## Doctrine de grain — quand splitter

Un commit doit pouvoir se résumer par **une phrase sans `et` ni `+`**. La présence d'un `+` dans le sujet est le signal le plus fiable de mégacommit déguisé : `chore: gitignore cleanup + untrack docs/archives` est deux commits, `refactor(playground): States/Primitive sections + native Play/Pause toggle` est deux commits. Chacune des intentions doit pouvoir vivre et être révertée seule.

Cas canoniques par typologie de chantier. **Refonte transverse type bascule EventSource** — un commit infrastructure (interfaces, base class, registration boot), puis un commit par module migré (intention claire : migrer ce module), puis un commit bascule des sinks legacy, puis un commit nettoyage des stubs. Pas de mégacommit final qui empile tout. **Bug fix** — un commit pour le fix, éventuellement un commit pour les tests si la couverture s'ajoute conjointement. Si le fix expose un refactor préalable nécessaire, le refactor est un commit séparé en amont. **Refonte UI** — un commit par surface refondue, jamais un dump de fin de journée. Le pass UX copy d'une page et la refonte structurelle de la même page sont deux commits. **Renommage de module ou de symbole exposé** — un commit pour le renommage seul (`refactor(catalog): rename Localization → Catalog`), ensuite le contenu fonctionnel ; cette discipline rend le renommage visible et l'épargne d'un revert qui annulerait du travail réel.

## Doctrine de grain — quand fusionner

Le pendant existe : un changement n'est pas atomique parce qu'il est petit, il l'est parce qu'il **forme une unité testable autonome**. Trois cas légitimes de fusion. **Signature et appelants** — modifier la signature d'une méthode publique et propager les appels dans le même commit, parce qu'un commit intermédiaire ne compilerait pas. **Ressource et consommation** — ajouter une clé `.resw` et la consommer dans le XAML correspondant, parce que la clé orpheline n'a aucun sens isolé. **Renommage de fichier et références** — déplacer un fichier et mettre à jour ses `using`, parce que le repo ne tient pas debout entre les deux.

Une modification de scope étranger entrée par mégarde dans un commit en cours **ne fusionne pas par opportunisme**. On défait avec `git restore --staged` ou `git reset`, on commit l'intention principale, puis on commit séparément la modification accessoire.

## Cas du refactor transverse

Quand un chantier touche plusieurs modules, deux stratégies. La voie **canonique** est le découpage par étape sémantique cross-module — un commit par module migré, avec le scope du module touché. Cette voie préserve la bisectabilité et raconte la progression du chantier. La voie **thématique**, plus rare, vaut quand l'opération est sémantiquement indissociable — par exemple un renommage atomique d'un symbole public consommé partout. Le scope est alors le thème cross-module (`refactor(catalog)`, `refactor(observability)`), et le commit reste unique parce que le découper produirait des états intermédiaires non compilants.

Le critère de choix : **est-ce que des commits intermédiaires laissent le repo dans un état qui compile et tient debout** ? Si oui, découper par module. Si non, scope thématique unique. La règle anti-mégacommit reste : ce commit unique reste l'unité sémantique minimale du changement, pas un dump.

## Merge commits

Stratégie projet : feature branches mergées dans `main` en `--no-ff`, jamais rebase squash. Le merge commit reçoit comme message `merge: <branch-name> — <résumé court de l'intention de la branche>`. Le résumé court est la phrase de couverture lisible dans `git log --oneline` ; les commits internes de la branche restent visibles via `git log <branch>` et sont la matière première de la bisectabilité.

La qualité d'un merge commit est **dérivée de la discipline interne de la branche**. Si les commits internes sont eux-mêmes des dumps composés ou ambigus, aucun résumé de merge n'y remédie. La responsabilité doctrinale est en amont, dans chaque commit individuel de la feature branch.

## Identité auteur

Tous les commits sortent sous l'identité du maintainer (`Louis <git@louisfifre.com>`). **Jamais** de trailer `Co-Authored-By: Claude <…@anthropic.com>`, **jamais** de ligne `🤖 Generated with [Claude Code](…)`. Ces marqueurs inscrivent Claude comme contributeur GitHub visible, ce qui est factuellement faux : un agent LLM n'est pas un contributeur au sens du contrôle de version. La règle est portée par le `CLAUDE.md` racine du projet ; elle est rappelée ici parce que c'est précisément l'acte de commit qui la met en jeu, et que c'est le moment où la tentation d'inscrire l'agent réapparaît.

## Trois signaux d'audit avant d'envoyer

Avant d'exécuter `git commit`, trois questions de relecture qui rattrapent la majorité des dérives observées dans l'historique Deckle. **Le sujet contient-il un `+` ou un `et`** qui joint deux intentions distinctes ? Splitter. **Le sujet dépasse-t-il 72 caractères sans qu'aucune intention puisse être retirée** ? C'est probablement deux commits camouflés en un. **Le scope est-il virgulé** ou imprécis (`(playground, ambient)`, `(misc)`) ? Choisir le scope principal et splitter l'autre intention, ou nommer un scope thématique légitime.

## Pointeurs

- **`git-commit`** (skill global) — mécanique d'exécution, analyse de diff, format Conventional Commits générique. `deckle-commits` précise la doctrine pour Deckle.
- **`personal-conventions`** — règles cross-projet (langue, conventions de branche, worktrees). `deckle-commits` applique au projet.
- **`deckle-docs`** — convention documentaire et ADR. Quand un commit acte une décision tracée, le body mentionne `refs ADR-NNNN`.
- **`deckle-modularite`** — frontières des modules. Les scopes des commits miroitent cette liste ; un scope qui n'y figure pas est un signal soit de scope inventé, soit de module manquant à promouvoir.
- **`deckle-nomenclature`** — vocabulaire des noms, dont les noms de modules qui servent de scopes.
- **`deckle-refonte`** — skill orchestrateur. Une refonte multi-volets séquencée par commits intermédiaires invoque ce skill pour la stratégie de découpage.
- **[conventionalcommits.org](https://www.conventionalcommits.org/en/v1.0.0/)** — spec normative de référence.
