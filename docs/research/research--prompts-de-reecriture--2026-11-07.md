---
description: "Ce que la boucle d'optimisation des prompts de réécriture et la campagne « prompt court » ont établi (6-7 novembre 2026, harnais benchmark/rewrite/model-compare v2, juge Opus 5.5) : les prompts optimisés battent les prompts de départ sur des dictées tenues à l'écart (Lissage 97,5 contre 73,8 ; Affinage 90 contre 67,5 ; Arrangement 62,5 contre 0 sur Sonnet), un Lissage raccourci à 850 ou 1 050 caractères perd le sens 35 fois sur 40 là où le prompt long tient 37 fois sur 40, avec des fautes systématiques qui correspondent une à une aux règles retirées ; la boucle ajoute des règles et n'en retire jamais ; les trois prompts installés dans les profils le 7 novembre, celui d'Arrangement n'étant mesuré que sur Sonnet alors que le profil tourne sur Opus."
---

# Prompts de réécriture — ce que la boucle d'optimisation et la campagne « prompt court » ont établi

Le harnais qui a produit ces chiffres (`benchmark/rewrite/model-compare`, commandes `run`, `optimize`, `summary`) est jetable : le mainteneur a décidé que le dossier benchmark se réécrit à la demande plutôt que de rester dans le dépôt. Cette note garde ce qui vaut la peine d'être su sans lui. Aucun texte de dictée n'y figure : les corpus sont les dictées du mainteneur.

Convention de lecture : **mesuré** (un chiffre pris par le harnais), **vérifié** (lu dans le code ou les résultats), **interprétation** (expliqué, pas prouvé), **incertain**.

## 0. Ce qu'il faut retenir

1. **Mesuré** — Les prompts sortis de la boucle d'optimisation battent nettement les prompts qui tournaient avant, sur des dictées que la boucle n'a jamais vues (score de 0 à 100 : fidèle 100, mineur 50, infidèle 0, moins 10 par réécriture plus longue que le brut ou portant du markdown).

   | Profil | Modèle | Prompt en vigueur avant | Meilleur tour | Taille du meilleur |
   |---|---|---|---|---|
   | Lissage | Opus 5.5 | 73,8 | 97,5 | 4 181 car. |
   | Affinage | Sonnet 5.5 | 67,5 | 90,0 | 7 207 car. |
   | Arrangement | Sonnet 5.5 (mesure) | 0 (40 refus sur 40) | 62,5 | 7 025 car. |

2. **Mesuré** — Raccourcir le prompt Lissage ne l'améliore pas, il le casse. Sur 40 réécritures (2 dictées moyennes et 2 longues, 10 passes chacune, Opus 5.5) : le prompt long de 2 212 caractères sort 37 fidèles, 2 mineures, 1 infidèle ; une version de 849 caractères sans les listes d'exemples sort 2 / 20 / 18 ; une version de 1 045 caractères qui autorise la reformulation libre sort 5 / 23 / 12. Mots gardés sur les longues : 98 % contre 83 % et 79 %.
3. **Vérifié** — Les fautes des prompts courts sont systématiques, pas du bruit : la même faute revient 8 à 10 fois sur 10 passes, et chacune correspond à une règle que le prompt long énonce. Le sujet échangé (une intention du locuteur devient un ordre à l'interlocuteur), le fragment complété à la place du locuteur, une négation ajoutée pour « réparer » une erreur de reconnaissance, un lien logique changé (« parce que » en « même si »), les nuances supprimées (« je crois que », « peut-être »), et sur les longues des options entières coupées.
4. **Interprétation** — Le coût du prompt long est négligeable, environ 600 jetons sur un appel qui en fait 2 000 à 5 000 ; la latence est identique, autour de 15 s. La liberté de forme déplace le modèle de la transcription vers l'interprétation : dès qu'il peut reformuler, il tranche les ambiguïtés et choisit ce qui mérite de rester. Une réécriture libre est un autre profil avec un autre contrat de fidélité, pas un Lissage raccourci.
5. **Vérifié** — La boucle d'optimisation ajoute des règles et n'en retire jamais : le score ignore la longueur du prompt, l'instruction « ne défais pas ce qui a monté le score » fait cliquet, et le proposeur ne voit que les fautes de la dernière manche. Elle impose aussi le français ; un prompt en anglais n'a jamais été mesuré. La suite décidée : une manche d'élagage de temps en temps (retirer une règle, garder le candidat seulement si le score tient) et un contrôle des données entre les manches, pas de relecture de cap qui ferait bouger la cible.
6. **Vérifié** — Le 7 novembre à 03h04, les trois meilleurs prompts ont été installés dans `modules\llm\settings.json` (sauvegarde du fichier d'avant dans `backups\llm-settings-20261107-030420.json`). Le prompt Arrangement n'est mesuré que sur Sonnet ; le profil tourne sur Opus, où la boucle s'était arrêtée après 6 réécritures sur 40. **Incertain** donc sur Opus, où une seule dictée de 30 minutes a donné un verdict mineur sans inversion de sens, avec le prompt court d'avant.

## 1. Méthode, pour la refaire

- **Génération** par le moteur de production (`ClaudeEngine`, effort bas), une cellule = un profil ou un fichier de prompt, n passes par dictée ; le prompt est haché par ligne de résultat, parce qu'un profil change de texte sous le même identifiant.
- **Juge** : Opus 5.5 en effort haut, par lots de 5 réécritures de la même cellule, qui relève par catégorie les substitutions de termes, inversions de sens, nuances tombées, omissions et ajouts, en citant les passages. Verdict fidèle / mineur / infidèle. Le juge est une piste, pas la vérité : lire les passages cités avant de croire un compte. Opus juge Opus, ce qui est un biais connu et non mesuré.
- **Boucle** : à chaque manche, un proposeur (Opus) réécrit le prompt à partir des fautes regroupées de la manche, sans copier de passage des dictées ; le candidat est généré, jugé, gardé s'il fait mieux ; les meilleurs sont validés à la fin sur des dictées tenues à l'écart. Budget en dollars par boucle, de 3 à 28 USD par profil ici.
- **Coûts** : la campagne « prompt court » a coûté 9,33 USD (120 réécritures et leur jugement) ; une génération Opus d'une dictée de 1 400 mots coûte 0,10 USD, son jugement autant.

## 2. Pièges rencontrés

- Choisir les dictées « les plus proches de la médiane » retombe sur celles qui ont entraîné la boucle : épingler des dictées tenues à l'écart avant de comparer.
- Un résumé qui groupe les verdicts sans l'itération compte dix fois chaque passe.
- Les prompts mesurés séparent les paragraphes par des sauts de ligne ; la zone de texte des réglages écrit des retours chariot nus. Pour envoyer au modèle ce qui a été mesuré, écrire le fichier de réglages à la main avec des sauts de ligne.
- Les résultats (textes compris) restent hors git ; seul le texte des prompts entre dans le dépôt.
