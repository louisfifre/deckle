---
name: deckle-modularite
description: Doctrine de modularité et de découpage pour le projet Deckle (Windows .NET 10 / WinUI 3). Porte les critères qui guident où s'arrête un module, quand un fichier devient trop gros, quels signaux indiquent un découpage à revoir, et comment scinder une surface UI devenue monolithique. Triggers on phrases like deckle modularité, scinder fichier deckle, scinder module deckle, refonte modulaire deckle, fichier trop gros deckle, responsabilité module deckle, dépendances modules deckle, découpage page deckle.
---

# Deckle — Doctrine de modularité

## Rôle

Skill projet-spécifique qui répond à deux questions : **où s'arrête un module**, et **quand un fichier devient trop gros pour rester confortable**. S'invoque avant d'ajouter du code substantiel à un module, avant de décider qu'un nouveau module est nécessaire, et avant de scinder un fichier devenu monolithique.

La doctrine cible deux objectifs joints. Faciliter le travail avec un agent LLM, qui repère plus facilement quel fichier est concerné quand l'arborescence est lisible et que les fichiers restent à taille humaine. Et faciliter la relecture par Louis a posteriori, étape par étape, sans avoir à charger en mémoire des fichiers de plusieurs milliers de lignes.

## Une responsabilité par module

Un module Deckle porte **une responsabilité claire et nommable en une phrase**. Si on a besoin de plus d'une phrase pour décrire ce que fait un module, c'est probablement qu'il en porte plusieurs et qu'il mérite d'être scindé en sous-modules. La responsabilité s'exprime en termes métier ou fonctionnels (« la capture audio depuis le microphone », « la transcription d'un blob audio en texte », « le pilotage des lampes externes »), pas en termes d'architecture (« le service », « le manager », « les helpers »).

Un module bien découpé a une **API publique étroite** vers le reste de l'app. Le détail de son implémentation peut être riche en interne, mais ce qu'il expose se compte. Quand un module a des dizaines de symboles publics, soit il porte plusieurs responsabilités, soit son API publique est sous-réfléchie.

## Dépendances acycliques entre modules

Les dépendances forment un **graphe orienté acyclique** : un module dépend de modules plus bas dans la hiérarchie (plus fondamentaux), jamais de modules au même niveau ou au-dessus. Quand un cycle apparaît, c'est un signal qu'il y a une mauvaise séparation de responsabilité — soit une notion partagée devrait remonter dans un module fondamental commun, soit deux modules devraient être fusionnés parce qu'ils sont en réalité une seule chose.

L'ordre des modules dans le graphe (des feuilles fondamentales vers l'app hôte) reflète aussi l'ordre logique pour les travailler — les feuilles d'abord, ce qui en dépend ensuite.

## Seuil de scission de fichier

Au-delà d'environ cinq cents lignes, un fichier mérite d'être examiné. Ce n'est pas une règle dure — un record de configuration immuable peut avoir mille lignes sans poser de problème, un fichier de glue peut être inconfortable à deux cents. C'est un seuil de **vigilance** : passé ce point, regarder si le fichier porte une seule responsabilité ou si plusieurs blocs sémantiques cohabitent.

Quand plusieurs blocs sémantiques cohabitent, les extraire dans des fichiers séparés du même module est presque toujours bénéfique. L'agent LLM repère plus facilement quel fichier est concerné par sa tâche, Louis voit dans la liste des fichiers touchés une trace plus précise de ce qui a changé, et la relecture devient gérable.

L'extraction se fait en suivant la responsabilité, pas par découpage arbitraire en quartiers. Un fichier de mille cinq cents lignes qu'on coupe en deux fichiers de sept cent cinquante lignes chacun sans rapport à la sémantique n'apporte rien. Un fichier de deux mille lignes qu'on éclate en quatre fichiers de cinq cents qui portent chacun un sous-rôle clair (l'état machine, les callbacks, l'instrumentation, la disposition propre) apporte beaucoup.

## La testabilité comme signal

Un module qu'on peut tester en isolation, sans dépendre de l'environnement d'exécution complet (fenêtre WinUI réelle, matériel physique, service externe), est probablement bien découpé. La logique métier vit dans des classes pures qui ne dépendent que d'interfaces ; les implémentations qui touchent au monde réel sont injectées et peuvent être remplacées par des doubles en test.

À l'inverse, quand on n'arrive pas à tester un module sans démarrer l'app entière, c'est un signal que les responsabilités sont mélangées — la logique pure est mêlée à du couplage de plateforme. La refonte consiste à extraire la logique pure vers un module fondamental qui ne dépend pas de la plateforme, et à laisser la couche de plateforme comme une fine façade.

## Découpage par sous-pages pour les fenêtres qui grossissent

Quand une fenêtre ou une page accumule des modes différents (un mode d'accueil, un mode de calibration, un mode de monitoring, un mode de configuration avancée), le code-behind unique devient inconfortable. Le pattern naturel est de **scinder en sous-pages navigables** — chaque mode devient une page autonome, la fenêtre devient un cadre de navigation qui sélectionne la page active. C'est ce que font les surfaces canoniques Windows pour les fenêtres riches.

Le découpage par sous-pages a un effet de bord positif : il rend chaque mode testable en isolation, parce que la sous-page peut être instanciée sans la fenêtre hôte. Et il sort la doctrine de modularité du domaine du code pur pour l'appliquer aux surfaces UI.

## Distinction logique versus présentation

Dans un module qui porte à la fois de la logique et une surface UI, **séparer les deux côtés**. Le code-behind d'une page ne doit pas porter de logique métier — il fait du wiring entre la surface visuelle et un objet métier (un view-model ou un service) qui porte la logique. Cette discipline rend la logique testable et rend la page UI mince. Quand on trouve de la logique métier dans un fichier `.xaml.cs`, c'est généralement un signe qu'il faut extraire vers un objet dédié.

## Comment scinder en pratique

Quand un fichier ou un module mérite d'être scindé, la séquence suivante est efficace.

D'abord, **identifier les blocs sémantiques** présents dans le fichier — les rôles internes que la responsabilité globale recouvre. Un agent peut aider à cette cartographie en lisant le fichier et en sortant un découpage candidat. Il faut au moins trois ou quatre blocs identifiables pour qu'une scission ait du sens.

Ensuite, **commencer par les blocs les plus indépendants** — ceux qui ont le moins de couplage avec les autres. Un helper statique qui ne lit aucun champ d'instance peut sortir tout seul ; une classe imbriquée qui ne dépend que de son enclosing class à travers une interface peut sortir avec son interface. Les blocs les plus couplés restent en place dans le fichier d'origine jusqu'à ce que leur extraction devienne possible.

Enfin, **après chaque extraction, valider que le code se compile et se comporte comme avant**. La discipline du build passe à chaque pas, pas seulement à la fin. Une régression introduite par une scission est plus facile à corriger immédiatement qu'après dix scissions cumulées.

## Pointeurs

- **`deckle-refonte`** — skill orchestrateur qui pointe vers ce skill quand un chantier touche au volet modularité.
- **`deckle-docs`** — quand une décision de scission non triviale est prise (notamment l'extraction d'une notion dans un nouveau module), elle mérite une entrée dans le journal du module concerné.
- **`deckle-logging`** — l'observabilité d'un module bien découpé est plus claire ; quand on scinde, profiter de l'occasion pour vérifier que les sites d'observation suivent la nouvelle frontière.
