---
name: journal-deckle
description: "Journal daté des décisions intermédiaires, hypothèses, learnings de session. Complément réversible aux ADRs (qui figent les décisions stables)."
type: project-journal
---

# Journal Deckle

## Pourquoi ce fichier

Les **ADRs** (`docs/adr/NNNN-*.md`) actent les décisions **stables** — une fois mergées, elles sont figées, et une révision crée un nouvel ADR qui supersede. C'est cher à produire, c'est définitif.

Beaucoup de décisions et de learnings de session sont plus légers : une piste explorée et abandonnée, un constat technique daté, une hypothèse pour la prochaine session, le contexte d'un choix qui aurait été perdu sinon. Ces choses méritent d'être notées **datées**, mais sans le poids cérémoniel d'un ADR.

Le journal accueille ça. Format : entrées chronologiques, datées `YYYY-MM-DD`, titre court, corps prose. **Réversibilité assumée** — on peut éditer, refondre, archiver les entrées vieillies au contraire des ADRs. Si une entrée devient une décision durable, elle est promue en ADR à ce moment-là.

Les entrées récentes sont en haut. À chaque nouvelle, ajouter au sommet, pas à la fin.

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
