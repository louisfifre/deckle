---
description: "Mesure (2026-11-21) du délai de fin de prise de 400 ms sur 113 prises brutes du corpus : 300 ou 200 ms couperaient du son dans 12 % et 22 % des prises, 600 ms n'aurait aidé que 2 prises. 400 ms est gardé."
---

# Délai de fin de prise : 400 ms confirmé

Question (audit du 2026-11-21, point 15) : les 400 ms pendant lesquels la capture continue après l'appui sur Stop (`CAPTURE_TAIL_AFTER_STOP_MS`) peuvent-ils baisser, ou doivent-ils monter ?

## Méthode

Outil : `benchmark/capture-tail/TailMeasure.cs`. Il lit seulement les échantillons des WAV du corpus et n'imprime que des totaux et des percentiles : aucun nom de fichier, aucun texte, aucune ligne JSONL n'est lue.

- Un WAV brut finit 400 ms après l'appui. Soit `trailing` le temps entre la dernière trame de 20 ms au-dessus du seuil et la fin du fichier. `trailing < 400` veut dire que du son courait encore à l'appui, et que la queue l'a rattrapé.
- Une queue de X ms (X ≤ 400) aurait coupé du son dans les prises où `trailing < 400 − X`.
- Prises retenues : écrites depuis le 2026-10-22 (la queue date du 2026-10-21), 184 fichiers. 71 ont l'air coupées par le VAD (début et fin à moins de 60 ms de la parole) : elles ne disent rien de l'appui et sont écartées. Restent **113 prises brutes**.

## Résultats (seuil -50 dBFS, 113 prises)

| Queue | Prises où du son serait coupé |
|---|---|
| 200 ms | 25 sur 113 (22 %) |
| 300 ms | 13 sur 113 (11 %) |
| 400 ms | 0 (référence : c'est ce qui est enregistré) |
| 600 ms | non rejouable ; 2 prises sur 113 (1,8 %) ont du son jusqu'à la toute fin du fichier, seules candidates à un gain |

Aux seuils -45 et -40 dBFS, les parts coupées à 300 ms tombent à 7 % et 3 %, à 200 ms à 17 % et 15 % : le sens ne change pas. Du son encore présent à l'appui : 26 %, 19 %, 16 % des prises selon le seuil.

## Ce qui est établi, interprété, non mesuré

- **Établi** : sur des prises brutes réelles, une queue plus courte laisse du son dehors dans une prise sur dix à une sur cinq. Une queue plus longue n'en rattraperait presque aucune (2 prises sur 113 au plus).
- **Interprétation** : ce « son » inclut des souffles et des fins de mot faibles. Tout n'est pas un mot perdu, mais le seuil le plus strict (-40 dBFS, qui ne garde que de la voix franche) coupe encore 3 % des prises à 300 ms.
- **Non mesuré** : les mots finaux perdus par le moteur (il faudrait rejouer Whisper sur chaque coupe et comparer ; cela se fait sans lire de texte, en ne comptant que les mots, mais n'a pas été fait ici) et le temps Stop vers texte, qui vaut par construction la queue plus le reste du pipeline. Le tri brut / coupé par le VAD vient d'une heuristique sur le silence au début et à la fin, pas des métadonnées du corpus.

## Décision

400 ms est gardé. Descendre coûterait des fins de prise ; monter ajouterait 200 ms d'attente pour presque aucun gain. Le commentaire de la constante cite cette mesure. Aucun changement de comportement.
