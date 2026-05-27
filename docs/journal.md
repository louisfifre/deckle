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

## 2026-05-27 — POC Voxtral, verdict provisoire et findings

**Bench voxtral-validation complet livré** sur 30 samples du corpus `voxtral-val-30` × 6 régimes T1-T6 (T1 baseline, T2 verbatim, T3 translate, T4 summary, T5 qa_register, T6 sys_prompt) avec judge Gemini multimodal. Run archivé sous `%LOCALAPPDATA%\Deckle\benchmark\runs\voxtral-poc-0001\`. Premiers résultats : Voxtral 24B Q4_K_M sort des transcriptions techniquement correctes au WER global, mais l'écoute humaine sur 27 samples révèle une perte systématique des nuances françaises — pronoms qui changent (je → tu), suffixes oubliés (« 0.3.1 » → « 0.3 »), termes techniques anglais incompris (`loadwindow` → « low wind », `clear` → « effacer »), réécriture en registre standard de l'oral spontané. L'anglais via T3_translate reste excellent, signe que c'est bien la génération française fine qui flanche, pas l'audio understanding.

**Trois agents recherche en parallèle** ont convergé sur deux causes : **(1)** étude Cohere [arXiv 2407.03211] qui chiffre la dégradation perception humaine FR au passage FP16→4-bit à **-16.6%** (vs -0.3% en métriques automatiques) — donc Q4_K_M sur 24B paie un coût caché énorme sur le français nuancé ; **(2)** `llama-mtmd-cli` n'a pas de mode transcription pur, il passe par le chat template Devstral hérité — le token `[TRANSCRIBE]` officiel Voxtral n'est pas injecté. Sur le test direct du token : notre prompt « Transcris cet audio en français. » contourne déjà efficacement le mode chat sur le 24B Q4 — ce n'est pas la cause principale de la dégradation observée. La cause principale est bien la quantization.

**Test croisé 24B Q4_K_M vs 3B Q8_0 sur 3 samples ciblés** confirme l'hypothèse Cohere. Le 3B Q8_0 (~98% qualité FP16) résout les deux erreurs majeures du sample S1 (« si je t'autorise » au lieu de « si tu t'autorises », « 0.3.1 » préservé). Améliorations partielles sur S2 (grammaire préservée). Mais : **dégénération en mode chat conversationnel sur le sample S0 court (1.7s)** — le 3B est plus sensible au défaut chat que le 24B. Les termes techniques anglais (`loadwindow`) restent ratés dans les deux versions — limite indépendante de la quantization, probablement liée au tokenizer Tekken et au manque de contexte d'apprentissage.

**Conversion locale safetensors → GGUF FP16 bloquée**. Trois tentatives ratées sur `convert_hf_to_gguf.py` (build llama.cpp b9310) : (a) sans `--mistral-format` → tokenizer Tekken non reconnu, `MistralCommonBackend has no attribute vocab` (peut-être lié à la version récente de `transformers` 4.x) ; (b) avec `--mistral-format` (lecture du `consolidated.safetensors` Mistral original) → tokenizer OK mais blocage sur le mapping tensor `mm_whisper_embeddings.tok_embeddings.weight` du mmproj mélangé au LM. Ni `ggml-org/Voxtral-Mini-3B-2507-GGUF` ni d'autres repos communautaires ne fournissent un FP16 préfait du LM (juste Q4_K_M et Q8_0). Voies à explorer prochaine session : downgrade transformers pour retrouver le vocab attendu, patcher le mapping tensor, ou alternative de stack (Transformers+DirectML directement, qui débordait sur 24B mais pourrait tenir sur 3B en VRAM).

**Refonte benchmarking livrée**. `lib/paths.py` sépare code worktree (`BENCHMARK_CODE_DIR`) et data persistante (`BENCHMARK_DATA_DIR` = `%LOCALAPPDATA%\Deckle\benchmark\`), expose `MODELS_DIR=D:\models\llm` pour ne pas dupliquer les GGUF entre worktrees, et fournit `make_run_dir(model, phase)` qui implémente le schéma de nommage `<modèle>-<phase>-<NNNN>` (phases : poc, debug, testing, integration). Viewer HTML générique sous `benchmark/viewers/build_html.py` avec auto-discovery des régimes et références — plus rien de hardcodé voxtral-validation. Doctrine actualisée dans `benchmark/CLAUDE.md`. Le précieux des sessions précédentes (`runs/voxtral-perf-cap-2026-05-26/` avec 31 logs cross-quants, corpus `voxtral-val-30` enrichi GT Gemini, notes utilisateur exportées) est rapatrié sous AppData et survit désormais aux worktrees.

**Direction prochaine session** : trouver une voie viable pour **Voxtral Mini 3B FP16** (l'hypothèse Cohere prédit qu'il devrait préserver bien plus que le Q8_0 sur la fidélité linguistique fine, surtout pour de l'audio dont les courbes sont fines), évaluer en parallèle **Gemma 3 multimodal** comme alternative open source (fenêtre native 30s, chunkable, latence interactive meilleure). Whisper reste le fallback stable même avec ses hallucinations connues et son VAD lent — il est ultra-rapide et la régression vs Whisper se fait sentir côté latence dès qu'on passe à Voxtral. La piste prompt `[TRANSCRIBE]` n'est plus prioritaire (vérifiée sans effet sur 24B Q4 dans le mode actuel) mais à re-tester sur 3B FP16 pour neutraliser la dégénération chat sur les samples courts.
