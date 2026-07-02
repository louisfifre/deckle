---
description: "Recherche vérifiée (deep-research, 2026-07-02) — comment mesurer un correcteur personnel pour décider : harnais offline par classe d'erreur, métriques online, architecture deux étages, personnalisation ; commandée au grill du chantier Correcteur de confiance."
type: research-report
---

# Correcteur personnel — l'évaluation comme colonne vertébrale

Commande de Louis (grill du 2026-07-02) : « je sais à peu près quoi construire ; il me manque comment mesurer pour prendre les bonnes décisions ». Cinq axes demandés — personnalisation, code-switching FR/EN, correction différée, UX de la confiance, et transversalement la méthodologie d'évaluation. Méthode : harnais deep-research, 5 angles de recherche parallèles, 21 sources primaires (ACL, EMNLP, arXiv, blogs d'ingénierie first-party), 104 claims extraits, les 25 plus porteurs vérifiés par 3 votes adverses chacun — 24 confirmés, 1 réfuté, 12 findings après fusion. Chaque affirmation ci-dessous porte ses réserves ; un finding est une piste sourcée, pas un verdict.

## 1. Le harnais offline — mesurer par classe d'erreur, sans annotation manuelle

**ERRANT (Bryant et al., ACL 2017) est la brique de référence.** Il extrait automatiquement les édits d'une paire phrase-originale/phrase-corrigée et les classe via ~50 règles déterministes (POS + lemmes, aucune donnée d'entraînement), ce qui débloque précision ET rappel par classe d'erreur — avant lui, seul le rappel était mesurable par type. Validation humaine : 5 experts, ≥95 % des types jugés Good/Acceptable. Réserve majeure pour Deckle : les règles sont spécifiques à l'anglais — un classifieur ERRANT-français est à trouver ou à construire (accords, conjugaisons, diacritiques, homophones grammaticaux). [P17-1074]

**Le harnais s'auto-alimente en texte parallèle brut.** Les références ré-annotées automatiquement par ERRANT sont statistiquement indistinguables des références gold humaines pour le scoring (bootstrap CoNLL-2014, p > .05) : il suffit de paires (texte tapé/dicté brut → texte corrigé validé), jamais d'annotation d'erreurs à la main. C'est exactement ce que `autocorrect.text.jsonl` et la télémétrie de transcription produisent déjà. Réserves : validé anglais-only et au niveau système ; la stabilité par classe sur un petit corpus personnel (quelques milliers de mots) est une inconnue ouverte. [P17-1074]

**Un score agrégé unique masque des échecs décisifs.** À CoNLL-2014, cinq systèmes sur douze n'ont corrigé AUCUNE erreur « token superflu » (~25 % des erreurs du test) — et l'un d'eux a fini 3e au classement global. L'évaluation se fait par classe, jamais par un chiffre unique. [P17-1074]

**Le socle historique M2 (Dahlmeier & Ng, NAACL 2012) apporte deux principes à retenir** même en partant d'ERRANT : le scoring multi-références (plusieurs corrections valides existent ; on score contre celle qui maximise le score du système) et F0.5 — la précision pèse deux fois le rappel, cohérent avec le coût asymétrique d'une fausse correction. [m2scorer, N12-1067]

## 2. Le protocole industriel — replay, métriques online, A/B

**Google (Gboard) : corpus-replay offline.** TypingTester rejoue des frappes enregistrées à travers le décodeur et mesure WER, précision de prédiction, temps de décodage. Le protocole de collecte compte autant que l'outil : les participants tapent vite avec pour consigne de NE PAS corriger leurs fautes, pour capturer la distribution d'erreurs naturelle. Transposition directe : enregistrer ses propres frappes/dictées brutes + leur version corrigée, puis rejouer chaque évolution du correcteur dessus. [2410.15575, 1704.03987]

**Métrique online phare : le Words Modified Ratio (WMR)** — proportion des mots committés que l'utilisateur modifie ensuite ; l'amélioration = réduction du WMR, utilisé comme proxy du WER sans vérité terrain. Pour un correcteur mono-utilisateur : instrumenter chaque correction avec son devenir (conservée / annulée via l'encart / ré-éditée) donne le même signal sans A/B de flotte. Les changements de décodeur Gboard se valident en A/B live avec IC 95 %, en suivant WMR + vitesse + latence. [2410.15575, 2209.11311]

**Grammarly formalise le même triptyque** : offline précision/rappel au niveau suggestion, online signaux d'engagement par suggestion (montrée, acceptée/ignorée/rejetée), et — point clé — le point de fonctionnement précision/rappel est choisi *empiriquement* : plusieurs variantes aux trade-offs différents partent en beta, l'engagement tranche. Leur North Star interne : 95 % d'exactitude « user-generated » (suggestion acceptée ET non annulée ensuite). Réserve : blog first-party de 2022, pré-virage LLM. [grammarly-innovating, grammarly-multiple]

## 3. L'étage phrase différé — validation quantitative et jugement

**L'architecture deux-étages est validée chiffres à l'appui** (Google, FSMNLP 2017, décodeur livré dans Gboard) : sur le même décodeur, activer la post-correction des mots déjà committés (le mot suivant comme contexte) fait chuter le WER tapping de 5,78 % à 5,07 % et gesture de 11,51 % à 9,20 %. Deux modérateurs d'agressivité documentés au commit : scorer la chaîne littérale avec un modèle caractère (protège les mots hors-vocabulaire — directement le cas du vocabulaire personnel et de l'anglais) et un coût explicite additionnel pour corriger un mot déjà valide (« auto-corrompre » du texte correct agace plus que laisser une typo). Réserves : éval petite, anglais, pas de test de significativité. [1704.03987]

**ATTENTION — claim réfuté 0-3** : « les post-corrections Gboard seraient limitées à une fenêtre temporelle du dernier mot + très haute confiance » ne survit pas à la vérification. Tout dimensionnement de la fenêtre de révision différée qui s'appuierait sur ce prétendu précédent repose sur du vide — notre frontière-phrase reste une décision propre à Deckle, à valider par nos propres mesures.

**Pour juger l'étage phrase, l'exact-match est rétrogradé** (plusieurs corrections valides existent) au profit du jugement par LLM : le « Good ratio » de Gboard Proofread = sortie sans erreur grammaticale ET préservant le sens, les deux vérifiées par un LLM sous instruction spécifique. Référence atteinte : PaLM2-XS fine-tuné SFT puis RL, 85,56 % de good ratio sur golden set humain. Le pattern LLM-judge est répliquable en local pour juger notre sentence stage ; le chiffre, lui, est auto-rapporté, anglais-centré, non transférable tel quel. [2406.04523]

**Les données synthétiques amorcent, les données réelles tranchent** (vote 2-1, le seul partagé) : Gboard fabrique des corpus par injection d'erreurs clavier-réalistes (omission, insertion, transposition, double tap, gaussiennes positionnelles) rejouées dans le vrai décodeur — mais l'alignement avec la distribution d'erreurs réelle est un objectif affirmé, jamais démontré. Pour Deckle : les logs réels de Louis sont la meilleure source de distribution ; le synthétique sert avant d'avoir du volume. [2406.04523]

## 4. Personnalisation — paramétrique et validée par utilisateur

**OPPU (EMNLP 2024)** : un module PEFT par utilisateur, stockant ses patterns dans des paramètres qu'il possède ; surpasse significativement la personnalisation par prompt sur les sept tâches LaMP. Soutient la faisabilité du modèle personnel fine-tuné envisagé à terme ; ne chiffre pas le cas correcteur. [2402.04401]

**L'adaptateur OOV de Meta est le pattern le plus proche du besoin vocabulaire personnel** : un MLP résiduel sur embeddings char-CNN des mots hors-vocabulaire les plus fréquents de l'utilisateur (jusqu'à 1000), entraîné localement — jusqu'à +5,6 % relatif en prédiction du mot suivant, ≥97 % de réduction du taux de mots inconnus. Son gabarit d'évaluation est transférable tel quel : split 8:1:1 des données de CHAQUE utilisateur (adapter sur train, early-stop sur validation, rapporter sur test tenu à l'écart), métrique agrégée pondérée par volume. Réserves : simulation FL sur Reddit/SO, venue légère, split non explicitement chronologique. [2305.03584]

## 5. Trous restants — ce que cette passe n'a PAS couvert

Deux axes demandés n'ont produit aucun claim survivant, par priorisation du budget de vérification (25 claims vérifiés sur 104 extraits) :

- **Code-switching FR/EN token-level** : sources identifiées mais non exploitées (LinCE LREC 2020 — benchmark LID token-level ; 1909.13016 ; EACL 2024). Question ouverte : approche établie (LID token-level, score caractère type Gboard, modèle dédié) et précision atteignable sur du FR majoritaire à îlots EN techniques.
- **UX de la confiance** : sources identifiées mais non exploitées (Auto-Cucumber GI 2022 — frustration sur fausses corrections ; Eiband IUI 2019 ; CHI 2017). Question ouverte : où se situe le point de rupture (taux de fausses corrections toléré avant désactivation) et quel design d'undo explicite le repousse. Seuls fragments indirects survivants : le coût Gboard pour corriger un mot valide, le North Star Grammarly « acceptée ET non annulée ».

Une passe ciblée sur ces deux axes (mêmes sources, vérification dédiée) est le prolongement naturel si le besoin se confirme au moment de construire le sentence stage et l'encart.

Autres questions ouvertes : existence d'un ERRANT-français (ou coût du portage des ~50 règles) ; taille minimale de corpus personnel pour des métriques par classe statistiquement stables, et quelles classes agréger en dessous.

## Sources principales

- ERRANT — Bryant, Felice & Briscoe, ACL 2017 : https://aclanthology.org/P17-1074.pdf
- M2 scorer — Dahlmeier & Ng, NAACL 2012 : https://aclanthology.org/N12-1067 ; https://github.com/nusnlp/m2scorer
- Gboard décodeur FST + post-correction — Hellsten et al., FSMNLP 2017 : https://arxiv.org/pdf/1704.03987
- Gboard TypingTester / WMR — EMNLP 2024 Industry : https://arxiv.org/pdf/2410.15575 ; personnalisation spatiale : https://arxiv.org/pdf/2209.11311
- Gboard Proofread — 2024 : https://arxiv.org/pdf/2406.04523
- Grammarly engineering : https://www.grammarly.com/blog/engineering/innovating-the-basics/ ; https://www.grammarly.com/blog/engineering/accepting-multiple-suggestions/
- OPPU — EMNLP 2024 : https://arxiv.org/abs/2402.04401
- Adaptateur OOV Meta — 2023 : https://arxiv.org/pdf/2305.03584
- Non exploitées (axes 2 et 4) : LinCE https://aclanthology.org/2020.lrec-1.223/ ; Auto-Cucumber https://graphicsinterface.org/wp-content/uploads/gi2022-16.pdf ; Eiband et al. IUI 2019 ; CHI 2017 https://dl.acm.org/doi/10.1145/3025453.3025695
