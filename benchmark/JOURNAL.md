---
name: journal-benchmark
description: "Journal daté du module benchmark : décisions intermédiaires, hypothèses, learnings de session. Complément réversible aux ADRs (qui figent les décisions stables)."
type: module-journal
---

# Journal — Benchmark Deckle

## Pourquoi ce fichier

Les **ADRs** (`docs/adr/NNNN-*.md`) actent les décisions **stables** — une fois mergées, elles sont figées, et une révision crée un nouvel ADR qui supersede. C'est cher à produire, c'est définitif.

Beaucoup de décisions et de learnings de session sont plus légers : une piste explorée et abandonnée, un constat technique daté, une hypothèse pour la prochaine session, le contexte d'un choix qui aurait été perdu sinon. Ces choses méritent d'être notées **datées**, mais sans le poids cérémoniel d'un ADR.

Le journal accueille ça. Format : entrées chronologiques, datées `YYYY-MM-DD`, titre court, corps prose. **Réversibilité assumée** — on peut éditer, refondre, archiver les entrées vieillies au contraire des ADRs. Si une entrée devient une décision durable, elle est promue en ADR à ce moment-là.

Les entrées récentes sont en haut. À chaque nouvelle, ajouter au sommet, pas à la fin.

---

## 2026-05-27 (suite 3) — Grille Q8_0 livrée, voie BF16 confirmée, bug judge désynchro révélé

Bench `voxtral-llamacpp-mini3b-q8-validation-0001` exécuté sur les 30 samples × 6 régimes avec judge Gemini activé. Run archivé sous `%LOCALAPPDATA%\Deckle\benchmark\runs\voxtral-llamacpp-mini3b-q8-validation-0001\`. Objectif : construire la grille à trois niveaux qualité/perf (BF16 Mini 3B / Q8_0 Mini 3B / Q4_K_M Small 24B) pour répondre à la question « est-ce qu'avec des bons prompts on peut avoir une qualité correcte sur un modèle plus petit ou plus quantizé ».

**Tableau T1_baseline cross-runs** (30 samples communs) :

| Run | WER médian | WER moyenne | stdev |
|---|---|---|---|
| BF16 Mini 3B (transformers, run-0001) | 0.257 | 0.308 | 0.21 |
| Q4_K_M Small 24B (voxtral-poc-0001) | 0.447 | 1.821 | 5.06 |
| Q8_0 Mini 3B (llamacpp, run-0001) | 0.451 | 1.329 | 3.15 |

Deux lectures convergent. À **modèle constant** (Mini 3B), passer de BF16 à Q8_0 perd ~0.19 de WER médian — quantization mesurable. À **quant comparable** (Q4_K_M Small 24B vs Q8_0 Mini 3B), résultats équivalents en médiane (0.45 vs 0.45) avec stdev plus basse côté Mini 3B Q8_0 (3.15 vs 5.06) — le 3B compense la taille par la fidélité de quantization.

**Pollution du signal Q8_0 par la pathologie chat-mode `llama-mtmd-cli`**. Sur 180 rows Q8_0, 17 dégénèrent (WER > 2.0), concentrés sur **4 samples courts** ≤ 2.3s. Cas le plus net : `dcad692a` (1.7s, « et toujours douter un peu. ») — Q8_0 sort une dissertation philosophique de 200+ mots (« Critique et Scepticisme », « Prudence »), tandis que BF16 sur le même sample était match parfait. C'est la signature documentée dans le finding 2026-05-27 du [CLAUDE.md du module](./CLAUDE.md) : `llama-mtmd-cli` n'injecte pas `[TRANSCRIBE]`, le Mini 3B est plus sensible que le Small 24B sur les courts, le modèle paraphrase ou commente. Cette pathologie est **structurelle au runtime**, pas à la quantization. Le BF16 via Transformers n'en souffre pas parce que `processor.apply_transcription_request` injecte le token implicitement.

**Conséquence : Q8_0 Mini 3B via `llama-mtmd-cli` non-déployable tel quel**. Les 4 samples courts contaminés (13% du corpus) tirent la moyenne et la stdev vers le haut. La perte « pure quantization » du Q8_0 est probablement inférieure à 0.19 — sans pouvoir l'isoler proprement tant que le runtime injecte un chat-mode par défaut. BF16 reste la cible POC.

**Verdict tranché par Louis** : Q8_0 abandonné, BF16 Mini 3B confirmé comme voie POC. La grille à trois niveaux atteint son objectif — confirmer que la dégradation FP16→4-bit prédite par Cohere se reproduit en interne, à la fois en taille (24B Q4 ≈ 3B Q8) et en quant (3B BF16 ≫ 3B Q8). Ouvre le chantier suivant : retrouver, par les prompts, ce que le modèle perd sur le baseline T1, façon initial prompt sticky Whisper.

**Bug judge prompt désynchronisé révélé et corrigé**. Le tableau « Régimes » de [`prompts/judges/gemini_per_row.md`](../prompts/judges/gemini_per_row.md) parlait encore de `V1_raw`, `V2_lisse`, `V3_fidele`, `V4_fidele_annote`, `V5_traduit_en`, `V_canonical`, `W0` — vocabulaire d'une version précédente du bench. Les régimes réels du bench `voxtral-validation` sont `T1_baseline`, `T2_verbatim`, `T3_translate`, `T4_summary`, `T5_qa_register`, `T6_sys_prompt`. Aucun mapping. Le juge Gemini recevait un nom de régime qu'il ne trouvait pas dans sa rubrique et improvisait — d'où les axes incohérents observés (T3_translate à `fidelite_signal` 90 alors que la sortie est en anglais, T4_summary à 100 alors que c'est un résumé, T5_qa_register à 50 alors que le régime ne demande pas de transcription).

Le prompt judge a été corrigé sur trois zones. Le tableau des régimes liste désormais T1-T6 avec ce qui est attendu de chaque sortie. L'axe `fidelite_signal` reçoit une **convention par régime** : pleinement applicable sur les régimes de transcription (T1, T2, T6), interprété en fidélité **sémantique** sur T3_translate et T4_summary, **non applicable et fixé à 100** sur T5_qa_register. Les cas particuliers en bas de fichier sont actualisés (T3_translate qui sort en français → regime_respecte 0 ; T5_qa_register qui transcrit du contenu → regime_respecte 0).

**Conséquence méthodo** : les axes paralinguistiques des runs antérieurs (`voxtral-poc-0001` Q4_K_M 24B, `voxtral-llamacpp-mini3b-q8-validation-0001` Q8_0 3B) sont **rétrospectivement contaminés** sur les régimes T2-T6. Les médianes WER restent valides (métrique objective, indépendante du juge). Pour les axes judge, les chiffres antérieurs sont à considérer comme du bruit calibré — utilisables comme intuition ordonnale (T1 mieux noté que T5), pas comme mesure absolue.

**Direction prochaine session** — à arbitrer par Louis : (a) commencer le chantier prompts BF16 façon initial prompt Whisper, en commençant par T1_baseline qui est le régime de référence pour la dictée ; (b) refaire passer le run BF16 T1 avec le judge corrigé pour avoir une baseline judge propre avant d'optimiser ; (c) instruire le sujet « système de gating amont » pour les fragments trop courts qui contaminent la mesure et qui contamineront aussi la production si on déploie tel quel.

**Apprentissage méthodo — désynchronisation silencieuse documentation/comportement**. Le prompt judge avait été écrit pour des régimes V1-V5 dans une itération précédente, puis le bench a été refondu en T1-T6 sans propagation au prompt. Le juge a continué à produire des réponses qui *avaient l'air* propres (JSON valide, scores cohérents les uns avec les autres) mais qui étaient en réalité de l'improvisation sur des entrées non documentées. La parade applicable : **toute refonte des régimes côté bench déclenche une relecture obligatoire des prompts judge et metric**. À ajouter à la doctrine `benchmark/CLAUDE.md` au moment opportun.

---

## 2026-05-27 (suite 2) — Résultats bench BF16 et apprentissages méthodo

Stack [ADR-0016](../docs/adr/0016-inference-safetensors-native-pour-voxtral.md) montée et bench T1_baseline exécuté sur les 30 samples du corpus `voxtral-val-30`. Run archivé sous `%LOCALAPPDATA%\Deckle\benchmark\runs\voxtral-transformers-validation-0001\`. Comparaison contre le run de référence Q4_K_M Small 24B (`voxtral-poc-0001`) via le script ad-hoc `benches/voxtral-transformers/compare_bf16_vs_q4.py`.

**Mesures objectives** (T1_baseline, 30 samples communs) :

| Métrique | BF16 Mini 3B | Q4_K_M Small 24B | Δ |
|---|---|---|---|
| WER médian | 0.257 | 0.447 | −0.189 |
| WER moyenne | 0.308 | 1.821 | −1.513 |
| WER stdev | 0.210 | 5.061 | −4.852 |
| `word_count_ratio` médian | 0.902 | 0.804 | +0.098 |
| RTF médian | 0.123 | 0.677 | −0.553 |

La WER moyenne Q4 à 1.82 traduit quelques runs hallucinant long (sortie qui boucle ou paraphrase). La stdev divisée par ~24 est le signal le plus net : BF16 est aussi beaucoup plus **stable**, pas seulement plus juste en médiane.

**Confirmation des patterns critiques annotés Louis** dans `voxtral-val-30-notes.json`. Trois samples mesurés en détail :

- `701ce47a` (29.2s, « VRAM + 8K ») — BF16 capte « RAM **et la VRAM** » et « contextes minimaux, **8K** » ; Q4_K_M dit « à fond **dans la RAM** » (VRAM omis) et omet le 8K.
- `e6db36e7` (54.2s, « je vs tu + 0.3.1 ») — BF16 dit « si **je t'autorise** explicitement à push » et « avec **0.3.1** » et « **bump de version** » ; Q4_K_M dit « si **tu t'autorises** », « avec **0.3** » (point), et lisse en « une version ».
- `dcad692a` (1.7s, « Et toujours douter un peu. ») — match parfait dans les deux versions, sample trop court pour discriminer.

L'hypothèse Cohere ([arXiv 2407.03211](https://arxiv.org/abs/2407.03211)) sur -16.6 % perception humaine FR au passage FP16→Q4 est **confirmée par mesure de terrain interne** sur le corpus Louis. Et confirmée avec un Mini 3B BF16 qui bat un Small 24B Q4_K_M, donc à modèle 5× plus petit — c'est exactement le sweet spot que la doctrine `benchmark/CLAUDE.md` finding 2026-05-27 anticipait.

**Pitfall stack actée** — `transformers 5.x` ré-introduit l'import `torch.distributed.tensor` via `transformers.generation.continuous_batching`. Le wheel `torch 2.9.1+rocm7.2.1` AMD pour Windows reste compilé `USE_DISTRIBUTED=0` et plante. Le [PR #40038](https://github.com/huggingface/transformers/pull/40038) qui a guardé l'import en 4.x ne couvre pas ce nouveau code path. Pin obligatoire : `transformers >=4.56, <5.0`. Le bug se manifeste à l'import de `VoxtralForConditionalGeneration` et bloque toute utilisation.

**Apprentissage méthodo n°1 — diagnostic vieillissant**. Le pivot `transformers + torch ROCm Windows` → `torch-directml` daté de fin mai 2026 reposait sur le bug d'import `torch.distributed.tensor`. Ce bug avait été guardé upstream par PR #40038 mergée le **2025-08-12**, soit ~9 mois avant ce pivot. Le diagnostic a vieilli silencieusement sans qu'on s'en aperçoive. La règle écrite en doctrine cross-project du `CLAUDE.md` racine (« Official sources first on a moving tech ») et appliquée par les agents recherche du 2026-05-27 a permis de retomber sur la voie viable.

**Apprentissage méthodo n°2 — agents redondants**. Les 4 agents recherche du 2026-05-27 ont retrouvé en partie ce qui était déjà documenté dans l'entrée « État cumulé » du même jour (Transformers + ROCm Windows comme cul-de-sac, état mistral.rs/candle, etc.). Posture pour la prochaine fois : avant de paralléliser des agents recherche, balayer `benchmark/JOURNAL.md`, `docs/adr/`, les sections finding des `CLAUDE.md` modulaires. La règle est désormais explicite dans la doctrine cross-project du `CLAUDE.md` racine.

**Mises à jour du statut des voies** (par rapport à la photo « État cumulé » plus bas) :

- `Transformers + PyTorch + ROCm Windows` : promu **voie active validée** (sanity check OK, perf RTF OK, bench complet 30 samples OK, qualité confirmée).
- `Transformers + torch-DirectML` : reste cul-de-sac documenté (maintenance mode officiel Microsoft).
- `llama-mtmd-cli + Voxtral 24B Q4_K_M` : reste cul-de-sac qualité (verdict Cohere confirmé).
- `llama-mtmd-cli + Voxtral Mini 3B Q8_0` : reste voie active utilisable comme référence intermédiaire de comparaison.
- Voies de déblocage GGUF FP16 (patch `convert_hf_to_gguf.py`, downgrade `mistral-common`) : désamorcées en priorité, restent ouvertes pour la phase d'embarquement production.

**Direction prochaine session** — au choix, à arbitrer par Louis : (1) compléter les régimes T2-T6 BF16 via `apply_chat_template` ; (2) lancer le bench Mini 3B Q8_0 30×6 pour avoir la grille à trois niveaux qualité/perf (BF16 / Q8_0 / Q4_K_M 24B) qui répond à la question « est-ce qu'il faut forcément une quantization moins agressive » ; (3) ré-exécuter T1 BF16 avec judge Gemini activé pour valider la mesure objective par une évaluation externe ; (4) commencer à instruire le sujet « comment embarquer Voxtral dans Deckle distribué » (Python lourd, candle pas prêt, mistral.rs ne couvre pas le 3B 2507).

---

## 2026-05-27 (suite) — Pivot safetensors-natif et reconfiguration des voies après agents recherche

Après l'écoute humaine fine du run `voxtral-poc-0001` (24B Q4_K_M) et le test croisé 3B Q8_0, la cause du verdict décevant est confirmée **structurelle** : llama.cpp impose les quants pré-faits par ggml-org, et la conversion locale `safetensors → GGUF FP16` reste bloquée (tokenizer Tekken non lu par `convert_hf_to_gguf.py` sans `--mistral-format`, tensors mmproj fondus au LM avec `--mistral-format`). Pivot : ne plus dépendre de llama.cpp comme runtime d'évaluation, bâtir un canal d'inférence safetensors-natif via la stack officielle Mistral. Cible directe Voxtral Mini 3B en BF16 source (~9.5 GB VRAM), ouverture sur Gemma 3 multimodal et autres modèles ensuite.

**Reconfiguration du terrain stack-side par quatre agents recherche.**

Le wheel `torch 2.9.1+rocm7.2.1` officiel AMD pour Windows est **rebloqué viable**. L'issue [ROCm/ROCm#5689](https://github.com/ROCm/ROCm/issues/5689) qui portait le blocage `torch.distributed.tensor` est closed COMPLETED depuis le 2025-12-16 : le fix est venu en amont via [transformers PR #40038](https://github.com/huggingface/transformers/pull/40038), mergé le 2025-08-12, qui guarde l'import problématique. Le wheel reste compilé `USE_DISTRIBUTED=0` mais `transformers ≥ 4.56` ne déclenche plus le code path bloquant. Le pivot DirectML de fin mai n'était plus nécessaire — on a hérité d'un diagnostic d'avant ce merge upstream. Référence install : [rocm.docs.amd.com — install pytorch](https://rocm.docs.amd.com/projects/radeon-ryzen/en/latest/docs/install/installrad/windows/install-pytorch.html).

`torch-directml` entre en **cul-de-sac documenté**. Le repo [microsoft/DirectML](https://github.com/microsoft/DirectML) affiche un bandeau *maintenance mode* officiel et renvoie vers Windows ML (Windows 11 24H2+) comme successeur. Dernière release [`0.2.5.dev240914`](https://pypi.org/project/torch-directml/) datée septembre 2024 — gel effectif ≈ 20 mois. Pin torch ~2.3 incompatible avec transformers ≥ 4.55 qui tire torch ≥ 2.4. Bugs VRAM RX 7900 documentés et fermés *not planned* ([microsoft/DirectML#412](https://github.com/microsoft/DirectML/issues/412), [#395](https://github.com/microsoft/DirectML/issues/395)). Voie morte.

**Stack officielle Mistral identifiée.** La [model card Voxtral-Mini-3B-2507](https://huggingface.co/mistralai/Voxtral-Mini-3B-2507) et la [doc transformers Voxtral](https://huggingface.co/docs/transformers/main/en/model_doc/voxtral) recommandent `transformers ≥ 4.54.0` + `mistral-common[audio] ≥ 1.8.1` + chargement BF16 via `VoxtralForConditionalGeneration.from_pretrained(repo, torch_dtype=torch.bfloat16, device_map=device)`. Le `[TRANSCRIBE]` token est **géré implicitement** par `processor.apply_transcription_request(language="fr", audio=path, model_id=repo)` qui délègue au `MistralCommonBackend` — exactement ce que `llama-mtmd-cli` rate. Pipeline audio : 16 kHz mono attendu (héritage `WhisperFeatureExtractor`), max 30 minutes en mode transcription, 32k context en mode understanding. BF16 plutôt que FP16 parce que c'est le dtype d'entraînement natif (cf. `config.json`), et la RX 7900 XT (RDNA3) supporte BF16 nativement.

**Économie disque immédiate disponible.** Le fichier `consolidated.safetensors` (9.35 GB) téléchargé dans `D:\models\llm\voxtral\Voxtral-Mini-3B-2507-safetensors\` est redondant avec les shards `model-00001-of-00002.safetensors` + `model-00002-of-00002.safetensors` (9.36 GB total) — `snapshot_download` télécharge les deux par défaut. Quand on utilise Transformers (qui lit `model.safetensors.index.json` + shards), `consolidated.safetensors` est supprimable sans casse. Il sert au framework `mistral-inference` standalone qui charge le monolithe, qu'on n'utilise pas.

**Pas de runtime natif C++/Rust viable aujourd'hui sur Windows AMD à part llama.cpp.** [mistral.rs](https://github.com/EricLBuehler/mistral.rs) v0.8.0 ne couvre que CUDA/Metal et ne supporte explicitement que Mini 4B Realtime, pas 3B 2507. [candle](https://github.com/huggingface/candle) supporte Voxtral 3B 2507 via [PR #3036](https://github.com/huggingface/candle/pull/3036) mergée août 2025 mais n'a ni Vulkan ni ROCm Windows (PR ROCm [#3424](https://github.com/huggingface/candle/pull/3424) WIP). Donc pour Deckle distribué à terme, on reviendra sur GGUF + llama.cpp Vulkan — mais on aura entre-temps validé proprement la **vérité de terrain Voxtral** via Transformers BF16, ce qui permettra de mesurer ce que les quants perdent vraiment.

**Mise à jour du statut des voies** (par rapport à la photo « État cumulé » plus bas, datée d'avant cette session) :

- `Transformers + PyTorch + ROCm Windows` : sort des cul-de-sacs, devient **piste ouverte à exécuter** dès la prochaine action (sanity check 1 sample en BF16). À promouvoir en voie active sitôt qu'un run termine.
- `Transformers + torch-directml` : entre dans les cul-de-sacs documentés (maintenance mode, pin torch obsolète, bugs VRAM non corrigés).
- `llama.cpp + Mini 3B Q8_0` reste utilisable comme référence de comparaison ; pas le runtime cible d'évaluation, mais le point de référence GGUF actuel.
- Les voies de déblocage GGUF FP16 (downgrade `mistral-common`, patch local du mapper `mm_*`) restent ouvertes mais **désamorcées en priorité** — l'inférence safetensors-native fait disparaître le besoin pour le POC. Elles redeviendront pertinentes au moment d'embarquer Voxtral dans Deckle distribué via llama.cpp.

**Direction prochaine étape concrète.** Setup venv Python 3.12 + wheel torch ROCm Windows officiel + `transformers` récent + `mistral-common[audio]`. Sanity check single-sample BF16. Si OK, intégration d'un backend `voxtral-transformers` dans le bench `voxtral-validation` et run 30×6 en BF16 pour produire la grille comparée Q4_K_M vs Q8_0 vs BF16.

---

## 2026-05-27 — État cumulé des voies explorées Voxtral

Synthèse à destination de la prochaine session, pour ne pas redécouvrir ce qui est déjà tranché. Trois colonnes mentales : **cul-de-sacs documentés** (ne pas retenter sans nouvelle info externe), **voies actives** (testées, partiellement utilisables), **pistes ouvertes** (jamais évaluées rigoureusement, à investiguer).

**Cul-de-sacs documentés** :

- **Transformers + PyTorch + ROCm Windows** sur RX 7900 XT. Bloqué dur. Le wheel `torch 2.9.1+rocm7.2.1` officiel AMD pour Windows est compilé avec `USE_DISTRIBUTED=0`. À l'import de `torch`, `torch.distributed.tensor` se charge immédiatement et plante sur le c10d manquant. Transformers ≥ 4.55 (toutes les versions Voxtral-compatibles) importe systématiquement ce sous-module via `AutoProcessor`. Voie patch-Python explorée jusqu'au bout, finit sur un opérateur C++ manquant non patchable. Documenté dans [ADR-0014 section pivot stack 2026-05-24](adr/0014-poc-evaluation-voxtral.md). Conditions de levée : AMD ship un torch ROCm Windows complet — suivi via [ROCm/ROCm#5689](https://github.com/ROCm/ROCm/issues/5689). Tant que cet issue est OPEN, ne pas retenter.
- **Transformers + torch-DirectML sur Voxtral Small 24B**. VRAM sature, le pipeline charge le modèle deux fois apparemment (mode DML particulier), le poste devient inutilisable. Pas viable sur le 24B. Le commit `bf48948 perf(bench): durcir les sources Voxtral DML contre l'inflation VRAM` a tenté du hardening sans succès sur le 24B.
- **Voxtral Mini 4B Realtime 2602**. Testé par Louis avant le POC formel via les PRs llama.cpp [#19698](https://github.com/ggml-org/llama.cpp/pull/19698) et [#20625](https://github.com/ggml-org/llama.cpp/pull/20625). Le mode streaming ne donnait pas de qualité satisfaisante. Variante laissée de côté.
- **`convert_hf_to_gguf.py` standard sur Voxtral safetensors HF** (sans `--mistral-format`). Tokenizer Tekken non lu → `MistralCommonBackend has no attribute vocab`. Probablement régression liée à la version récente de `transformers` qui a refondu la classe wrapper Mistral. Voie possible via downgrade `transformers` à une version pré-1.x mais pas testée.
- **`convert_hf_to_gguf.py --mistral-format` sur Voxtral consolidated**. Tokenizer OK mais blocage sur `Can not map tensor 'mm_whisper_embeddings.tok_embeddings.weight'` — le mode `--mistral-format` traite les tensors mmproj mélangés au LM, et le mapping n'a pas d'entrée pour eux. Voie possible via patch local du mapper pour filtrer les `mm_*` quand on convertit le LM seul, mais le script fait 3000 lignes et le patch n'est pas trivial.
- **Token `[TRANSCRIBE]` officiel Voxtral injecté manuellement dans le prompt mtmd-cli**. Testé sur Voxtral 24B Q4_K_M : aucun effet visible sur un sample court avec contenu propre. Notre prompt baseline « Transcris cet audio en français. » contourne déjà efficacement le mode chat sur le 24B (P0 = P1 verbatim parfait). Le token reste **à re-tester sur le 3B** où le mode chat dégénère sur les samples courts — peut-être qu'il aurait un effet là.

**Voies actives, qualité validée ou utilisable** :

- **`llama-mtmd-cli` + Voxtral 24B Q4_K_M + mmproj F16** sur stack Vulkan RX 7900 XT. Perf excellente (RTF 0.05-0.5 sur long-form). Qualité FR insuffisante sur les nuances (pronoms, suffixes, termes techniques EN, registre oral) — confirmé par bench 180 rows + écoute humaine 27 samples + étude Cohere [arXiv 2407.03211] qui chiffre -16.6% perception humaine FR au passage FP16→Q4 (vs -0.3% en métriques automatiques). **Pas adopté pour la dictée.**
- **`llama-mtmd-cli` + Voxtral Mini 3B Q8_0 + mmproj Q8_0** (officiels ggml-org). Q8_0 ~98-99% qualité FP16 selon la littérature. Testé sur 3 samples ciblés : résout `je→tu` et `0.3.1` préservé sur S1, améliore la grammaire fine sur S2, **dégénère en chat conversationnel sur S0 court** (le 3B est plus sensible au mode chat que le 24B). Résout les patterns critiques de Louis mais introduit le problème chat-court. **Utilisable à creuser**.
- **Whisper.cpp large-v3 + initial prompt sticky** (stack actuelle Deckle). Stable, ultra-rapide. Limites connues : hallucinations sur quasi-silence (« Sous-titrage ST' 501 », « Sous-titrage Société Radio-Canada »), VAD lent (irritant en interactif), bouclage occasionnel sur dictée longue, traduction faible de certains termes oraux français. **Reste le fallback déployé**.

**Pistes ouvertes à évaluer prochaine session** :

- **`llama-mtmd-cli` + Voxtral Mini 3B FP16** (le saint Graal — hypothèse Cohere prédit qu'il préserve nettement plus de fidélité audio fine que Q8_0). Conversion locale bloquée pour l'instant (deux voies dans les cul-de-sacs ci-dessus, mais chacune potentiellement débloquable avec effort modéré).
- **Transformers + torch-DirectML sur Voxtral Mini 3B** (le 3B FP16 = 6 GB, le 24B faisait déborder, mais le 3B tient en VRAM avec marge). Jamais essayé sérieusement. Si la conversion GGUF FP16 reste bloquée, c'est la voie de contournement la plus directe.
- **`llama-mtmd-cli` + Voxtral Small 24B Q5_K_M ou Q6_K**. Perf déjà mesurée en session perf-cap 2026-05-26 (logs sous `runs/voxtral-perf-cap-2026-05-26/`). Qualité jamais évaluée. Cohere suggère que Q5/Q6 sur 24B serait significativement meilleur que Q4 (-1% à -2% perception humaine au lieu de -16.6%) tout en restant en VRAM (16 GB pour Q5_K_M). Test rapide via le bench voxtral-validation actuel avec juste un swap de path modèle.
- **Gemma 3 multimodal** (Google open source, fenêtre native 30s, chunkable plus petit pour meilleure latence interactive). Aucune mesure faite. Évaluation parallèle valide si Voxtral 3B FP16 ne perce pas. À investiguer : disponibilité GGUF + support mtmd-cli + qualité FR.

---

## 2026-05-27 — POC Voxtral, verdict provisoire et findings

**Bench voxtral-validation complet livré** sur 30 samples du corpus `voxtral-val-30` × 6 régimes T1-T6 (T1 baseline, T2 verbatim, T3 translate, T4 summary, T5 qa_register, T6 sys_prompt) avec judge Gemini multimodal. Run archivé sous `%LOCALAPPDATA%\Deckle\benchmark\runs\voxtral-poc-0001\`. Premiers résultats : Voxtral 24B Q4_K_M sort des transcriptions techniquement correctes au WER global, mais l'écoute humaine sur 27 samples révèle une perte systématique des nuances françaises — pronoms qui changent (je → tu), suffixes oubliés (« 0.3.1 » → « 0.3 »), termes techniques anglais incompris (`loadwindow` → « low wind », `clear` → « effacer »), réécriture en registre standard de l'oral spontané. L'anglais via T3_translate reste excellent, signe que c'est bien la génération française fine qui flanche, pas l'audio understanding.

**Trois agents recherche en parallèle** ont convergé sur deux causes : **(1)** étude Cohere [arXiv 2407.03211] qui chiffre la dégradation perception humaine FR au passage FP16→4-bit à **-16.6%** (vs -0.3% en métriques automatiques) — donc Q4_K_M sur 24B paie un coût caché énorme sur le français nuancé ; **(2)** `llama-mtmd-cli` n'a pas de mode transcription pur, il passe par le chat template Devstral hérité — le token `[TRANSCRIBE]` officiel Voxtral n'est pas injecté. Sur le test direct du token : notre prompt « Transcris cet audio en français. » contourne déjà efficacement le mode chat sur le 24B Q4 — ce n'est pas la cause principale de la dégradation observée. La cause principale est bien la quantization.

**Test croisé 24B Q4_K_M vs 3B Q8_0 sur 3 samples ciblés** confirme l'hypothèse Cohere. Le 3B Q8_0 (~98% qualité FP16) résout les deux erreurs majeures du sample S1 (« si je t'autorise » au lieu de « si tu t'autorises », « 0.3.1 » préservé). Améliorations partielles sur S2 (grammaire préservée). Mais : **dégénération en mode chat conversationnel sur le sample S0 court (1.7s)** — le 3B est plus sensible au défaut chat que le 24B. Les termes techniques anglais (`loadwindow`) restent ratés dans les deux versions — limite indépendante de la quantization, probablement liée au tokenizer Tekken et au manque de contexte d'apprentissage.

**Conversion locale safetensors → GGUF FP16 bloquée**. Trois tentatives ratées sur `convert_hf_to_gguf.py` (build llama.cpp b9310) : (a) sans `--mistral-format` → tokenizer Tekken non reconnu, `MistralCommonBackend has no attribute vocab` (peut-être lié à la version récente de `transformers` 4.x) ; (b) avec `--mistral-format` (lecture du `consolidated.safetensors` Mistral original) → tokenizer OK mais blocage sur le mapping tensor `mm_whisper_embeddings.tok_embeddings.weight` du mmproj mélangé au LM. Ni `ggml-org/Voxtral-Mini-3B-2507-GGUF` ni d'autres repos communautaires ne fournissent un FP16 préfait du LM (juste Q4_K_M et Q8_0). Voies à explorer prochaine session : downgrade transformers pour retrouver le vocab attendu, patcher le mapping tensor, ou alternative de stack (Transformers+DirectML directement, qui débordait sur 24B mais pourrait tenir sur 3B en VRAM).

**Refonte benchmarking livrée**. `lib/paths.py` sépare code worktree (`BENCHMARK_CODE_DIR`) et data persistante (`BENCHMARK_DATA_DIR` = `%LOCALAPPDATA%\Deckle\benchmark\`), expose `MODELS_DIR=D:\models\llm` pour ne pas dupliquer les GGUF entre worktrees, et fournit `make_run_dir(model, phase)` qui implémente le schéma de nommage `<modèle>-<phase>-<NNNN>` (phases : poc, debug, testing, integration). Viewer HTML générique sous `benchmark/viewers/build_html.py` avec auto-discovery des régimes et références — plus rien de hardcodé voxtral-validation. Doctrine actualisée dans `benchmark/CLAUDE.md`. Le précieux des sessions précédentes (`runs/voxtral-perf-cap-2026-05-26/` avec 31 logs cross-quants, corpus `voxtral-val-30` enrichi GT Gemini, notes utilisateur exportées) est rapatrié sous AppData et survit désormais aux worktrees.

**Direction prochaine session** : trouver une voie viable pour **Voxtral Mini 3B FP16** (l'hypothèse Cohere prédit qu'il devrait préserver bien plus que le Q8_0 sur la fidélité linguistique fine, surtout pour de l'audio dont les courbes sont fines), évaluer en parallèle **Gemma 3 multimodal** comme alternative open source (fenêtre native 30s, chunkable, latence interactive meilleure). Whisper reste le fallback stable même avec ses hallucinations connues et son VAD lent — il est ultra-rapide et la régression vs Whisper se fait sentir côté latence dès qu'on passe à Voxtral. La piste prompt `[TRANSCRIBE]` n'est plus prioritaire (vérifiée sans effet sur 24B Q4 dans le mode actuel) mais à re-tester sur 3B FP16 pour neutraliser la dégénération chat sur les samples courts.
