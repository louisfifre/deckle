---
name: deckle-settings-ux
description: Doctrine d'expérience utilisateur pour les surfaces de paramétrage du projet Deckle (Windows .NET 10 / WinUI 3). Porte les principes d'information architecture (defaults sensés, progressive disclosure, settings vs commands, staged disclosure pour les pages conditionnelles, application immédiate sans bouton de validation, customization comme coût) ancrés sur les sources étayées Nielsen Norman Group et la doctrine officielle Microsoft. Triggers on phrases like deckle settings UX, deckle UX paramètres, organiser settings deckle, page settings deckle, qu'est-ce que j'expose deckle, hiérarchiser settings deckle, progressive disclosure deckle, settings vs commands deckle, page dynamique deckle.
---

# Deckle — Doctrine d'expérience pour les surfaces de paramétrage

## Rôle

Skill projet-spécifique qui répond à une question : **qu'est-ce qu'on expose à l'utilisateur dans les surfaces de paramétrage, et comment on l'organise**. S'invoque avant d'ajouter un paramètre exposé, de réorganiser une page, de décider de cacher ou de promouvoir une option, ou de refondre une surface qui a accumulé de la complexité au fil du temps.

La doctrine s'applique aux pages de paramètres, à la fenêtre de logs, à la barre de titre, à toute surface où l'utilisateur configure ou consulte l'état du système. Elle ne décrit pas le rendu visuel — la cohérence visuelle est portée par les primitives natives Windows. Elle décrit ce qui mérite d'apparaître, où, et avec quel poids.

## Defaults sensés avant customization

La grande majorité des utilisateurs ne touche jamais aux paramètres ; ceux qui le font sont une minorité. La conséquence est que **chaque paramètre exposé est une dette UX, pas une fonctionnalité**. L'arbitrage par défaut est de choisir le bon comportement par défaut, pas d'exposer un réglage. Quand un paramètre semble nécessaire, la première question est : peut-on le déduire ou l'inférer automatiquement ? Si oui, le système le fait et l'utilisateur n'a rien à choisir. Si non, on l'expose, mais on choisit la valeur par défaut comme si c'était la seule possible — l'utilisateur ne devrait pas avoir besoin de la modifier pour que l'app fonctionne bien dans son cas d'usage le plus courant.

## Progressive disclosure à deux niveaux maximum

L'utilisateur ne doit jamais avoir à descendre plus de deux niveaux pour atteindre un paramètre. Au-delà, la lisibilité de la navigation s'effondre — c'est une règle reconnue qui dit que les designs au-delà de deux niveaux de divulgation progressive ont une utilisabilité faible parce que les utilisateurs se perdent en revenant en arrière.

Concrètement, le premier niveau est la navigation top-level qui groupe les surfaces par thème. Le second niveau est la divulgation à l'intérieur d'une page — les options moins fréquentes vivent dans un repli qui s'ouvre sur demande. Au-delà, le signal est qu'on devrait refondre l'information architecture, pas ajouter un troisième niveau. **Les replis ne s'imbriquent jamais.**

## Settings versus commands

Une distinction cadrante souvent mal respectée. Un **paramètre** est une configuration persistante qui modifie le comportement futur de l'application. Une **commande** est une action immédiate qui agit sur le contexte courant.

L'inflation des paramètres exposés vient souvent de la confusion entre les deux. « Exporter les logs » est une commande, pas un paramètre. « Lancer la calibration » est une commande, pas un paramètre. « Réinitialiser ce groupe d'options » est une commande, pas un paramètre. Les commandes vivent dans des boutons d'action, dans des menus contextuels, dans des dialogues — pas dans la liste des paramètres exposés. Garder cette distinction propre allège considérablement les pages de configuration.

## Staged disclosure pour les pages conditionnelles

Quand un paramètre a un sens uniquement dans un certain contexte (un autre paramètre activé, un module configuré, un appareil détecté), il **n'est pas affiché grisé, il n'est pas affiché**. Le pattern reconnu en information architecture des UI complexes dit que les options ne sont montrées à l'utilisateur que lorsqu'elles sont pertinentes pour la tâche en cours ou pour l'objet sélectionné.

Une page qui devient hyper-dynamique avec de nombreuses inter-dépendances doit être lue comme un signal d'application incorrecte de cette doctrine — soit la page mélange plusieurs préoccupations qui mériteraient d'être séparées, soit l'arbre conditionnel est trop touffu pour rester compréhensible. La refonte d'une telle page passe par : identifier les axes indépendants (un paramètre n'en gouverne qu'un seul autre), regrouper les options qui co-varient avec le même axe, et faire apparaître ou disparaître conditionnellement plutôt que griser.

## Application immédiate sans bouton de validation

Norme Microsoft sur Windows : quand l'utilisateur modifie un paramètre, l'application reflète immédiatement le changement. Pas de bouton « Enregistrer », pas de bouton « Appliquer », pas de bouton « OK » qui valide une session de modifications.

L'implication côté code est une persistance auto-save et un retour visuel immédiat. L'implication côté UX est qu'il faut un mécanisme léger d'annulation (au moins par session, idéalement par paramètre) plutôt qu'un mécanisme lourd de validation explicite. Les rares cas qui méritent une validation explicite (paramètres dont l'erreur a un coût lourd, ou actions qui ont une conséquence externe) basculent dans la catégorie des commandes — pas dans la catégorie des paramètres.

## Customization a un coût

Distinction posée par les sources NN/g : la **customization** donne le contrôle à l'utilisateur (« choisissez le thème »), la **personnalisation** l'exécute pour lui (« on a détecté votre thème système »). La customization a un coût d'usabilité réel — les utilisateurs rencontrent fréquemment des difficultés quand ils essaient d'accomplir des activités de customization.

C'est un argument supplémentaire pour réduire la surface de paramètres exposés et privilégier des adaptations système automatiques quand c'est possible. La valeur par défaut « suivre le système » est presque toujours la bonne pour les options qui touchent à l'apparence (thème, langue, contrastes).

## Distinction sémantique des options

Pour deux options qui paraissent visuellement similaires (par exemple deux toggles côte à côte, deux items de liste), expliciter clairement ce qui les différencie. Chaque option a un libellé court et une description qui précise ce qu'elle fait, pas ce qu'elle est. Les libellés sont courts et factuels ; la description porte l'effet attendu, pas la justification technique.

## Aller chercher la matière, pas tout exposer

Avant de refondre une surface, faire un **inventaire factuel** de ce qui est aujourd'hui exposé et de ce qui est persisté mais caché. La refonte consiste autant à **enlever** qu'à organiser. Beaucoup d'options accumulées au fil du temps n'ont plus de raison d'être ou peuvent devenir des comportements par défaut. La refonte est aussi l'occasion d'identifier les commandes déguisées en paramètres et de les remettre dans la bonne catégorie.

## Surface de logs et fenêtre de diagnostic

La fenêtre de logs et toute surface de diagnostic suivent les mêmes principes — defaults sensés, divulgation progressive maîtrisée, distinction entre paramètres (filtres persistants) et commandes (recherche live, copie, export). Une vue de catalogue ou de schéma se consulte, ne se configure pas — elle relève donc plus du diagnostic que du paramétrage et a sa place dans la fenêtre de logs plutôt que dans les paramètres.

## Pointeurs

- **`deckle-refonte`** — skill orchestrateur qui pointe vers ce skill quand un chantier touche au volet UX.
- **`deckle-docs`** — quand une décision UX non triviale est prise, elle mérite une entrée dans le journal du module concerné.
- Les sources canoniques (NN/g sur la divulgation progressive et la customization, doctrine Microsoft sur les paramètres d'app Windows) sont à consulter en dehors de ce skill quand un cas dépasse le périmètre couvert ici.
