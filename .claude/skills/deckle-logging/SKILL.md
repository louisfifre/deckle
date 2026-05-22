---
name: deckle-logging
description: Doctrine d'observabilité pour le projet Deckle (Windows .NET 10 / WinUI 3). Porte la centralisation de l'émission, la séparation des niveaux (détails techniques structurés versus phrases concises lisibles), et la procédure de décision pour savoir quoi observer dans un bout de code que l'on instrumente. La taxonomie détaillée des observables (cadres canoniques USE, RED, Four Golden Signals appliqués au profil Deckle) vit dans le fichier compagnon taxonomy.md chargé à la demande. Triggers on phrases like deckle logging, observabilité deckle, qu'est-ce que je logue ici deckle, niveau de log deckle, instrumentation deckle, télémétrie deckle, observer du code deckle, logger un événement deckle.
---

# Deckle — Doctrine d'observabilité

## Rôle

Skill projet-spécifique qui répond à deux questions : **quoi observer dans un bout de code qu'on instrumente**, et **comment l'écrire pour que ce soit lisible et exploitable**. S'invoque avant d'ajouter un point d'observation, de modifier un niveau, ou de réorganiser ce qui est émis quelque part.

L'aide à la décision en amont (« qu'est-ce qui mérite d'être observé ici ? ») est le cœur du skill. La norme d'écriture (« comment je formule cet événement ? ») en est la seconde face. La doctrine est invariante au moteur technique sous-jacent — elle reste vraie quel que soit le système d'émission choisi.

## Doctrine de centralisation

Toute observation runtime passe par **une source d'émission unique**. Pas de chemin parallèle dans le code applicatif — pas d'écriture fichier ad hoc, pas de sortie console, pas de logger dupliqué. Si une observation mérite d'exister, elle passe par la source canonique ; si on a besoin qu'elle atterrisse à un nouvel endroit, c'est un sink supplémentaire enregistré auprès de la source canonique, pas un nouveau chemin d'émission.

La règle existe parce que maintenir deux ou trois chemins parallèles fait inévitablement diverger les formats, la nomenclature, les niveaux et les oublis. Le système central existe précisément pour ne pas avoir à gérer deux systèmes séparés.

Signal de dérive à reconnaître chez soi : dès qu'apparaît une intention de « créer un logger pour X » ou « écrire dans un fichier dédié pour Y », poser la question préalable — est-ce que la source canonique ne couvre pas déjà ce besoin via un sink ou un canal supplémentaire ? Dans la quasi-totalité des cas, oui.

**Exception unique et subordonnée** : le crash natif non rattrapable qui tue le process avant que les sinks aient pu écrire. Pattern d'instrumentation ad hoc fichier-écrit-direct, **temporaire**, jamais commité en l'état. Pour tout le reste, la règle de centralisation tient.

## Doctrine de séparation des niveaux

Deux familles distinctes coexistent et ne se mélangent jamais.

**La famille concise lisible** — informations, succès, avertissements, erreurs, critiques. Phrases courtes, techniques mais simples, lues comme des jalons par un humain qui suit le déroulé. Pas de notation `clé=valeur`, pas d'identifiants techniques en clair, pas de mesures chiffrées à l'intérieur du texte. Le contenu décrit ce qui se passe à l'étape : « Chargement du modèle », « Enregistrement terminé », « Connexion impossible au service », etc. La concision est essentielle — pas de phrases élaborées, pas de fioriture, juste le jalon.

**La famille détail structurée** — le niveau verbose. Reçoit les mesures, identifiants, paramètres techniques, latences, dimensions, codes de retour. Format structuré machine-greppable, plusieurs lignes possibles si on veut grouper sémantiquement, premier mot en minuscule pour bien distinguer visuellement des jalons de la famille précédente. Quand on instrumente une opération, on capte tout ce qu'elle expose d'observable, on le groupe en 3-4 lignes verbose par opération, pas une ligne par variable. La couverture maximale est privilégiée — le tri par niveau et par filtre se fait côté affichage ou côté requête, pas à l'émission.

**Articulation des deux familles.** Selon la séquence du code, le détail verbose précède ou suit le jalon concis. Quand le verbose capte les paramètres qui mènent à une décision (par exemple détecter une condition d'erreur), il précède le jalon d'information ou d'alerte qui en découle. Quand le verbose détaille les mesures associées à un jalon (par exemple les durées d'une opération qui vient de se terminer), il suit le jalon. Cette articulation rend la séquence narrative naturelle à lire dans la fenêtre live.

**Trois pièges récurrents à éviter** : mettre du `clé=valeur` dans une phrase de la famille concise (signe qu'on devrait sortir un verbose miroir) ; multiplier les jalons de la famille concise pour une même opération (signe qu'on devrait fusionner en un seul jalon avec un verbose détaillé) ; oublier d'instrumenter en verbose une étape qu'on a annoncée en jalon (signe qu'on perd la matière diagnostique).

## Doctrine de couverture maximale

Le maintainer veut **beaucoup de logs, bien triés**. Quand on instrumente une étape, exposer toutes les mesures observables, pas juste le minimum. Le tri par niveau, par catégorie, par filtre se fait à la lecture ou côté UI. Sous-instrumenter aujourd'hui pour économiser un peu de bruit revient à devoir réinstrumenter demain quand le diagnostic d'un bug arrive. Sur-instrumenter coûte peu si le filtrage est correct.

Cette doctrine se conjugue avec la possibilité d'activer ou désactiver des familles entières d'observation à chaud — un toggle général, plus quelques toggles par sous-système particulièrement bavard (capture haute fréquence, télémétrie microphone, corpus utilisateur). Tout est instrumenté ; on choisit ensuite ce qu'on regarde et ce qu'on persiste.

## Procédure de décision

Quand on s'apprête à instrumenter un bout de code, quatre questions dans l'ordre.

**Quel module est concerné.** L'observation s'attache au module qui contient l'opération, pas au module qui appelle. Cette attribution conditionne où l'événement apparaît dans la cartographie d'ensemble.

**Quelle catégorie de code est en train d'être instrumentée.** Boucle temps réel haute fréquence ? Pipeline ponctuel ou batch ? Driver de matériel ou intégration externe ? Surface d'interface utilisateur ? Cycle de vie d'application ? La catégorie détermine quels cadres canoniques s'appliquent et quels observables sont pertinents.

**Quels observables sont pertinents pour cette catégorie.** C'est là que la référence `taxonomy.md` est chargée et consultée : elle donne les cadres canoniques de l'industrie (utilisation, saturation, erreurs pour les ressources ; taux, erreurs, durée pour les pipelines ; latence, trafic, erreurs, saturation pour les surfaces qui servent) et leur application au profil Deckle, avec les sous-natures d'observables et des exemples.

**Quels événements typés en sortir.** Pour chaque observable pertinent, décider du niveau et de la formulation. Les détails techniques structurés en verbose, les jalons concis en information ou avertissement ou erreur. Si l'opération a un récapitulatif de fin (une ligne par opération avec tous ses champs colocalisés), c'est ce récapitulatif qui porte la vraie matière diagnostique — les jalons intermédiaires en jalonnent la lecture mais ne dupliquent pas son contenu.

## Vocabulaire fermé

Les sources d'observation, les unités de mesure, les noms d'opération sont des vocabulaires fermés — pas de création ad hoc, pas de variation orthographique. Si une unité manque (parce qu'on observe une grandeur jamais observée jusqu'ici), l'ajouter au vocabulaire canonique avant utilisation, pas à l'intérieur du nouvel événement seulement. La discipline du vocabulaire fermé garantit qu'un humain ou un agent qui filtre sur un terme retrouve exactement la même chose partout.

## Pointeurs

- **`taxonomy.md`** dans ce skill — cadres canoniques d'observabilité et mapping vers les catégories de code rencontrées dans Deckle. Chargé à la demande quand la procédure de décision atteint l'étape « quels observables ».
- **`deckle-docs`** — doctrine documentaire et hygiène des commentaires. Quand une décision d'instrumentation est non triviale, elle mérite une entrée dans le journal du module concerné.
- **`deckle-refonte`** — skill orchestrateur qui pointe vers ce skill quand un chantier touche au volet observabilité.
- **`personal-conventions`** — convention transverse de centralisation du logging (source unique, sinks interchangeables). `deckle-logging` applique cette convention au projet Deckle et la précise.
