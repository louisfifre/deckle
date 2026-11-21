---
description: "Mesure (2026-11-21) du délai de fin de prise de 400 ms : Whisper rejoué sur 196 prises brutes coupées à 300 et 200 ms, comparé à un contrôle. L'écart n'est pas distinguable du bruit de Whisper, le gain est de 100 à 200 ms : 400 ms est gardé faute de preuve nette."
---

# Délai de fin de prise : 400 ms gardé, preuve insuffisante pour changer

Question (audit du 2026-11-21, point 15) : les 400 ms pendant lesquels la capture continue après l'appui sur Stop (`CAPTURE_TAIL_AFTER_STOP_MS`) peuvent-ils baisser, ou doivent-ils monter ?

## Méthode

Outil : `benchmark/capture-tail` (projet `CaptureTail`, deux commandes). Il ne lit que les échantillons des WAV et n'imprime que des totaux, des parts et des percentiles : aucun nom de fichier, aucun texte, aucune ligne du corpus n'est affiché ni enregistré. Les mots d'une transcription n'existent qu'en mémoire, le temps de les compter.

- **Prises** : les captures brutes écrites depuis le 2026-10-21 (la queue date de ce jour). « Brute » vient du champ `audio_content` de chaque ligne du corpus, lu en mémoire (deux champs gardés, la ligne est jetée), et non d'une heuristique sur le silence comme dans la première version de cette note : 220 prises brutes, contre 113 retenues alors. Les 28 lignes « processed » (coupées par le VAD) et 89 lignes sans étiquette sont écartées.
- **Rejeu** (`replay`) : le vrai `WhisperBackend` de Deckle, modèle large-v3, français, réglages livrés par défaut (pas ceux du mainteneur : ni son prompt initial, ni ses profils), GPU Vulkan. Chaque prise est transcrite telle qu'enregistrée (la référence, 400 ms), puis rejouée : une seconde fois à l'identique (**contrôle de bruit**), coupée de 100 ms (queue de 300), coupée de 200 ms (queue de 200), et allongée de 200 ms de silence numérique (« 600 », voir plus bas).
- **Métrique** : seulement les 4 derniers mots, car une différence au milieu d'une dictée de 20 s ne dit rien de la queue. « Dernier mot différent » : le dernier mot de la référence n'est pas celui de la variante. « Mots finaux manquants » : parmi les 4 derniers mots de la référence, ceux qu'on ne retrouve pas dans les 6 derniers mots de la variante.
- **Sous-ensemble utile** : seules les prises où la zone coupée contient du son (au moins -45 dBFS dans les 100 ou 200 dernières ms) entendent quelque chose de différent. Sur les autres, la variante reçoit les mêmes mots avec un peu moins de silence.
- Deux séries de 120 puis 100 prises (dans l'ordre d'écriture) ; les parts sont additionnées. Au total 196 prises comparées, 24 écartées car Whisper a interrompu la référence (garde contre les boucles de répétition).

## Résultats (196 prises)

| Variante | Dernier mot différent, toutes prises | Dernier mot différent, prises avec son dans la zone coupée | Mots finaux manquants, toutes prises |
|---|---|---|---|
| 400 rejoué (contrôle) | 25 sur 196 (12,8 %) | sans objet | 69 sur 784 (8,8 %) |
| 300 ms | 27 sur 196 (13,8 %) | 5 sur 44 (11,4 %) | 66 sur 784 (8,4 %) |
| 200 ms | 29 sur 196 (14,8 %) | 10 sur 57 (17,5 %) | 66 sur 784 (8,4 %) |
| 600 ms (silence ajouté) | 29 sur 196 (14,8 %) | sans objet | 70 sur 784 (8,9 %) |

Le son dans la zone coupée : 44 prises sur 196 à 300 ms, 57 sur 196 à 200 ms (22 % et 29 %).

Temps Stop vers texte (queue plus calcul de Whisper) : la médiane vaut 0,9 à 1,6 s selon la série, car le calcul de Whisper varie de 0,7 à 1,2 s d'une série à l'autre (charge de la machine). La queue y compte pour un chiffre fixe : 100 ms de moins à 300, 200 ms de moins à 200, soit 6 à 20 % de la médiane. Le calcul de Whisper ne dépend pas de la queue de façon lisible dans ces séries.

Mesure audio seule (commande `energy`, 220 prises brutes, seuil -50 dBFS) : du son présent à l'appui dans 37 % des prises, au bout du fichier dans 19 %. À 300 ms, du son serait coupé dans 26 % des prises, à 200 ms dans 32 % : à -40 dBFS, 17 % et 26 %. C'est un majorant, car ce « son » compte les souffles et les bruits de pièce.

## Ce qui est établi, interprété, non mesuré

- **Établi** : Whisper ne se répète pas à l'identique sur le même audio (le dernier mot change dans environ 13 % des prises, le texte entier dans la moitié). Toute comparaison se lit donc contre ce contrôle. À 300 ms, le dernier mot diffère aussi souvent que dans le contrôle (13,8 % contre 12,8 %), y compris sur les prises où du son est coupé (11,4 %).
- **Établi, mais fragile** : à 200 ms, le dernier mot diffère un peu plus (14,8 % contre 12,8 %, et 17,5 % sur les prises avec du son dans la zone coupée). Les deux séries se contredisent sur ce sous-ensemble (1 sur 30 puis 9 sur 27) : avec 57 prises, l'incertitude est de l'ordre de 10 points, plus grande que l'écart cherché.
- **Interprétation** : le son coupé est en grande partie du souffle ou de la fin de mot faible, que Whisper complète de toute façon. Une queue de 300 ms ne montre aucune perte au-delà du bruit ; 200 ms reste possible mais non démontré.
- **Non mesuré** : (1) un allongement. Aucun audio n'existe après les 400 ms enregistrés ; la ligne 600 ms n'ajoute que du silence numérique et montre seulement que du silence en plus ne change rien de mesurable (14,8 % contre 12,8 %, dans le bruit). Elle ne peut pas montrer un gain. Mais 19 % des prises ont du son jusqu'au bout du fichier à -50 dBFS (13 % à -40) : dans ces prises, une queue plus longue entendrait peut-être des mots, et rien ici ne le dit. (2) Les réglages du mainteneur (prompt initial, profils, VAD du flux en direct) : le rejeu est monolithique avec les réglages livrés. (3) Les 24 prises écartées par la garde de répétition de Whisper.

## Décision

400 ms est gardé, par prudence : la règle était de ne changer que si la mesure le montre nettement. À 300 ms, aucune perte n'est visible mais le gain n'est que de 100 ms ; à 200 ms, le gain est de 200 ms mais une perte d'une dizaine de points sur le dernier mot n'est pas écartée. Pour trancher, il faudrait plus de prises avec du son dans la zone coupée (bien plus que 57 : l'ordre de grandeur n'a pas été calculé), ou un contrôle rejoué plusieurs fois par prise. Le commentaire de la constante cite cette mesure. Aucun changement de comportement, donc pas de test de non-régression.
