---
name: deckle-refonte
description: Panoramic refonte coordination skill for the Deckle project (Windows .NET 10 / WinUI 3). Holds the posture that six interlocking volets — observabilité, testing, surfaces UX, modularité, documentation, hygiène des commentaires — se nourrissent et ne se séquencent pas. Pointe vers les sous-skills doctrinaux (deckle-logging, deckle-testing, deckle-settings-ux, deckle-modularite, deckle-docs). Triggers on phrases like deckle refonte, grande refonte deckle, passe panoramique deckle, refonte observabilité deckle, refonte settings deckle, refonte logging deckle, refonte UX deckle, scinder modules deckle, inventaire deckle, chantier multi-volet deckle.
---

# Deckle — Refonte panoramique

## Rôle

Skill orchestrateur pour les chantiers Deckle qui dépassent un seul volet. Tient une posture pédagogique simple : **les volets ne se séquencent pas, ils se nourrissent**. Pointe vers les sous-skills qui portent chacun la doctrine de leur domaine. Ne duplique pas leur contenu — c'est une boussole, pas une encyclopédie.

S'invoque au début d'un chantier qui touche plus d'un volet, ou pour rappeler la posture quand la session glisse vers du mono-volet (« on commence par tel volet et on verra les autres après »).

## Les six volets indissociables

**Observabilité.** Une seule source d'émission pour tout ce qui s'observe à l'exécution, des sinks interchangeables qui rendent l'observation à différents endroits (fichier, fenêtre live, surface utilisateur). La doctrine de jugement vit dans `deckle-logging`.

**Testing.** Filet de sécurité pour la refonte et le quotidien. Pyramide adaptée à une app desktop client mono-process. Pattern de test natif quand l'observabilité est typée. La doctrine vit dans `deckle-testing` (en cours de cadrage par Louis).

**Surfaces UX.** Settings, fenêtre de logs, barre de titre responsive. Defaults sensés avant customization, divulgation progressive maîtrisée, application immédiate sans bouton de confirmation. La doctrine vit dans `deckle-settings-ux`.

**Modularité.** Une responsabilité par module, un seuil de scission au-delà duquel un fichier mérite d'être éclaté, la testabilité comme signal d'un bon découpage. La doctrine vit dans `deckle-modularite`.

**Documentation.** Trois fichiers canoniques par module — un pour décrire, un pour instruire l'agent, un pour journaliser les décisions. Racine du dépôt minimaliste. La doctrine vit dans `deckle-docs`.

**Hygiène des commentaires.** Pourquoi plutôt que quoi, vérité actuelle vérifiée, promotion vers le journal du module quand un commentaire mérite trace historique. La doctrine vit dans `deckle-docs` (section dédiée).

## Pourquoi en parallèle, pas en séquence

La cohérence se trouve dans le croisement des volets, pas dans leur enchaînement. Trois exemples qui le montrent.

Une refonte d'observabilité crée des points typés ; le testing les valide ; la surface UX rend leur schéma consultable. **Faire les trois en parallèle n'est pas un cumul, c'est un seul geste avec trois faces.** Si on enchaîne observabilité puis tests, on découvre au moment d'écrire les tests que la granularité ne convient pas et on refait.

Une page UX devenue dynamique au-delà du raisonnable est exactement le cas que la doctrine UX décrit ; la refondre exige de toucher au logging du module concerné et à ses tests en même temps, sinon on remet de la dette pendant qu'on en enlève.

La scission d'un fichier devenu monstrueux exige de décider quels événements il émet, quels tests valident chaque morceau, et comment l'UX retombe sur les patterns canoniques. Trois volets, une décision unique.

## Quand un module est livré

Un module entre en refonte avec ses six volets. Il en sort quand chacun d'eux a été traité dans son périmètre. **Pas d'avant. Pas de PR mono-volet.** La discipline du panoramique se joue dans la définition de fin d'un module, pas dans une feuille de route séparée par volet.

## Sous-skills

- **`deckle-logging`** — doctrine de jugement de l'observation, séparation des niveaux, procédure pour décider quoi observer dans un bout de code.
- **`deckle-testing`** — doctrine de testing pour app desktop client (à rédiger une fois Louis aura cadré sa stack).
- **`deckle-settings-ux`** — doctrine de surfaces de paramétrage et d'information architecture.
- **`deckle-modularite`** — doctrine de découpage, seuils, signaux.
- **`deckle-docs`** — convention documentaire projet-local et hygiène des commentaires.

## Pointeurs

- **`personal-conventions`** — conventions transverses (langue, wording, git, worktrees, nomenclature documentaire universelle). `deckle-refonte` applique ces conventions au projet Deckle, ne les remplace pas.
- Le fichier d'instructions agent à la racine du projet porte la doctrine cross-projet et les règles non négociables (jamais de build ou publish lancé par Claude, identité de commit du maintainer seul).

## Posture pédagogique

Louis apprend à développer en construisant. La refonte panoramique est une occasion de **prendre du recul** sur un état qui fonctionne mais qui a accumulé de la dette implicite. La posture du chantier est : faire l'inventaire avant de proposer, pas l'inverse. Quand un volet semble simple, vérifier qu'il l'est vraiment à la lumière des cinq autres. Quand un volet semble bloqué, regarder si la décision attendue vient d'un autre volet. À l'issue de la refonte, Louis relit le code et demande l'explication de chaque étape — le code doit donc être scindé proprement, nommé clairement, commenté quand le pourquoi mérite trace, et chaque décision non triviale doit avoir laissé une entrée dans le journal du module concerné.
