---
name: adr-0016-inference-safetensors-native-pour-voxtral
description: "Acte l'adoption de l'inférence safetensors-native via Transformers + torch ROCm Windows pour le POC Voxtral, après confirmation par mesure de terrain que BF16 récupère les nuances FR que Q4 perd (hypothèse Cohere)."
type: adr
---

# ADR-0016 — Adopter l'inférence safetensors-native pour le POC Voxtral

**Status** — accepted le 2026-05-27

Cet ADR enrichit [ADR-0014](./0014-poc-evaluation-voxtral.md) côté **stack d'inférence du POC**. ADR-0014 reste en vigueur sur le périmètre POC et le verdict pendant (whisper.cpp vs Voxtral). Le présent ADR tranche un choix orthogonal — *quelle pile fait tourner Voxtral pendant la phase d'évaluation* — sur la base de blocages structurels rencontrés en mai 2026 et d'un débloquage upstream découvert après les agents recherche du 2026-05-27.

## Contexte

Le POC Voxtral via `llama-mtmd-cli` ([ADR-0014](./0014-poc-evaluation-voxtral.md)) a rendu son premier verdict — Voxtral Small 24B Q4_K_M ne passe pas en transcription FR. L'analyse cause-racine pointe la **quantization** : l'étude Cohere [arXiv 2407.03211](https://arxiv.org/abs/2407.03211) chiffre la dégradation perception humaine FR au passage FP16→4-bit à -16.6 % alors que les métriques automatiques ne rapportent que -0.3 %. Le test croisé Mini 3B Q8_0 confirme la direction : Q8_0 (~98 % qualité FP16) résout des patterns critiques que Q4_K_M cassait.

La voie naturelle — tester Voxtral Mini 3B en **FP16/BF16 source** pour valider l'hypothèse Cohere — bute sur un blocage **structurel** côté llama.cpp. La conversion locale `safetensors → GGUF FP16` via `convert_hf_to_gguf.py` (build b9310) échoue par deux voies, chacune potentiellement débloquable au prix d'un effort non trivial : sans `--mistral-format`, `MistralCommonBackend has no attribute vocab` (régression `mistral-common` vs version du script) ; avec `--mistral-format`, blocage sur le mapping tensor `mm_whisper_embeddings.tok_embeddings.weight` (mmproj audio fondu au LM). Aucun repo communautaire ne publie le LM FP16 préfait — seulement Q4_K_M et Q8_0.

`llama.cpp` impose donc mécaniquement de tester sur des quants pré-faits. On hérite des erreurs spécifiques à chaque quant, sans pouvoir confronter la version source — celle qui valide ou réfute proprement l'hypothèse. Sortir du blocage exige soit d'investir des heures sur la conversion (avec un résultat incertain), soit de **ne plus dépendre de llama.cpp comme runtime d'évaluation**.

Côté Python natif sur Windows AMD, le terrain a bougé. Fin mai 2026, un pivot précédent avait acté que `transformers ≥ 4.55` + wheel `torch 2.9.1+rocm7.2.1` AMD officiel pour Windows plantait à l'import (`USE_DISTRIBUTED=0` côté wheel, mais `transformers` importait inconditionnellement `torch.distributed.tensor`). Le pivot avait basculé sur `torch-directml`, qui débordait la VRAM sur Voxtral Small 24B (double chargement) et n'a jamais été essayé sérieusement sur Mini 3B. Les agents recherche du 2026-05-27 ont découvert que [transformers PR #40038](https://github.com/huggingface/transformers/pull/40038) a guardé l'import bloquant le **2025-08-12** et que [ROCm/ROCm#5689](https://github.com/ROCm/ROCm/issues/5689) a été closed COMPLETED le 2025-12-16 sur cette base. Le pivot DirectML n'était plus nécessaire au moment où il a été fait — diagnostic obsolète sans qu'on s'en aperçoive.

Enfin la doc officielle Mistral ([model card Voxtral-Mini-3B-2507](https://huggingface.co/mistralai/Voxtral-Mini-3B-2507), [doc transformers Voxtral](https://huggingface.co/docs/transformers/main/en/model_doc/voxtral)) recommande explicitement deux voies : vLLM en serveur OpenAI-compatible, et **Transformers ≥ 4.54 + `mistral-common[audio]`** pour l'inférence locale. Cette deuxième voie matche directement l'objectif POC : safetensors source, BF16, single-machine. Elle utilise `processor.apply_transcription_request(language="fr", audio=...)` qui injecte implicitement le token `[TRANSCRIBE]` officiel — ce que `llama-mtmd-cli` rate (voir finding 2026-05-27 dans `benchmark/CLAUDE.md`).

## Options considérées

- **A. Continuer à débloquer la conversion `safetensors → GGUF FP16`.** Patcher `convert_hf_to_gguf.py` localement pour filtrer les tensors `mm_*` en mode `--mistral-format`, ou downgrader `mistral-common` à la version attendue par b9310. Effort moyen à élevé, résultat incertain. Préserve la pipeline bench llama.cpp existante. Garde le coupling au runtime d'évaluation que la fin du POC voudra découpler de toute façon.
- **B. Transformers + torch-DirectML sur Mini 3B.** Voie de contournement Python qui ne dépend pas de la conversion GGUF. Agent recherche du 2026-05-27 a documenté que `torch-directml` est en *maintenance mode* officiel chez Microsoft (renvoi vers Windows ML 24H2+), dernière release `0.2.5.dev240914` datée septembre 2024, pin torch ~2.3 incompatible avec `transformers ≥ 4.55`, bugs VRAM RX 7900 fermés *not planned*. Voie morte.
- **C. Transformers + torch ROCm Windows officiel.** Wheel `torch 2.9.1+rocm7.2.1-cp312-cp312-win_amd64.whl` AMD officiel, `transformers >=4.56,<5.0` (PR #40038 mergée, transformers 5.x ré-introduit le bug via `continuous_batching`), `mistral-common[audio] >= 1.8.1`. C'est la stack que la doc Mistral recommande. Validation par sanity check : modèle chargé en 8.7 GiB VRAM (sur 20 GiB dispo), RTF mesuré 0.11 long-form sur RX 7900 XT, sortie correcte sur sample test.
- **D. Runtime natif C++/Rust embarquable** (mistral.rs, candle, ollama). Agent recherche du 2026-05-27 a documenté que `mistral.rs` v0.8.0 ne supporte que CUDA/Metal et Voxtral Mini *4B Realtime* (pas le 3B 2507), `candle` supporte 3B 2507 via [PR #3036](https://github.com/huggingface/candle/pull/3036) mais sans backend Vulkan ni ROCm Windows. Aucun runtime natif n'est aujourd'hui viable sur Windows AMD pour Voxtral 3B 2507. La question reviendra au moment d'embarquer Voxtral dans Deckle distribué — pour l'instant elle est hors scope du POC d'évaluation.

## Décision

**Option C.** L'évaluation Voxtral passe à `Transformers + torch ROCm Windows` sur les safetensors source, conformément à la doc officielle Mistral. La stack épinglée du POC devient :

- `torch 2.9.1+rocm7.2.1` (wheel officiel AMD pour Windows, Python 3.12 strict)
- `transformers >=4.56,<5.0` — la 4.57.6 est la version retenue ; pin majeur < 5.0 parce que `transformers 5.x` ré-introduit l'import bloquant `torch.distributed.tensor` via le module `continuous_batching` (régression non couverte par PR #40038)
- `mistral-common[audio] >= 1.8.1` — fournit le tokenizer Tekken officiel et la pipeline audio attendue
- `accelerate` (requis par `device_map`)
- `librosa` (requis par `load_audio_as` côté transformers ; `mistral-common[audio]` ne le tire pas automatiquement)
- Modèle chargé en **BF16** (dtype d'entraînement natif, supporté par RDNA3) via `VoxtralForConditionalGeneration.from_pretrained(path, dtype=torch.bfloat16, device_map="cuda")` — sous ROCm Windows, l'alias `cuda` mappe HIP
- API d'inférence : `processor.apply_transcription_request(language="fr", audio=path, model_id="mistralai/Voxtral-Mini-3B-2507")` pour le mode transcription pure (gère implicitement le token `[TRANSCRIBE]`) ; `apply_chat_template` pour les modes à instruction (régimes T2-T6, à implémenter dans une seconde passe)

La cible safetensors locale est `D:\models\llm\voxtral\Voxtral-Mini-3B-2507-safetensors\` (shards `model-00001-of-00002` + `model-00002-of-00002`). Le `consolidated.safetensors` du même dossier sert à `mistral-inference` standalone qu'on n'utilise pas — supprimable sans casse (9.35 GiB libérables).

Le runtime de production embarquable dans Deckle distribué reste **question ouverte** et hors scope du présent ADR. Une fois le POC tranché côté qualité, un ADR séparé tranchera entre : repackager Python embarqué + Transformers, attendre `candle` Vulkan/ROCm Windows, attendre `mistral.rs` couverture Windows AMD, ou rebrancher sur GGUF llama.cpp avec un quant à fidélité acceptable.

## Conséquences

L'évaluation POC ne dépend plus de la conversion `safetensors → GGUF FP16`. Les deux voies de déblocage (patch `convert_hf_to_gguf.py`, downgrade `mistral-common`) restent ouvertes mais désamorcées en priorité — elles redeviendront pertinentes au moment de choisir le runtime de production embarqué.

La voie `Transformers + torch-DirectML` est actée comme **cul-de-sac documenté** (voir entrée 2026-05-27 dans `benchmark/JOURNAL.md`) et ne doit pas être ré-évaluée sans signal externe (Microsoft sortant DirectML de maintenance mode, ce qui n'est pas attendu).

Le bench `benches/voxtral-validation/` accepte désormais un argument `--source` qui switche entre `voxtral-llamacpp` (legacy GGUF Vulkan) et `voxtral-transformers` (BF16 ROCm). Le run nommage devient `<source>-validation-NNNN` pour distinguer les runs cross-backend. Le contrat `Source` ([`benchmark/lib/sources/_base.py`](../../benchmark/lib/sources/_base.py)) n'est pas modifié.

Première mesure de validation, 30 samples T1_baseline sur le corpus `voxtral-val-30`, exécutée le 2026-05-27 :
- BF16 Mini 3B WER médian 0.257, stdev 0.21
- Q4_K_M Small 24B WER médian 0.447, stdev 5.06 (la stdev élevée traduit des hallucinations longues occasionnelles)
- BF16 RTF long-form 0.11 (largement viable pour la dictée interactive ; Whisper.cpp à ~0.05-0.10 comme référence sur la même machine)
- Confirmation samples critiques annotés Louis : BF16 récupère « VRAM », « 8K », « je t'autorise », « bump de version », « 0.3.1 » que Q4_K_M omet ou réécrit. L'hypothèse Cohere est confirmée par mesure de terrain sur le corpus interne.

Le venv legacy `.venv-voxtral-dml/` sous `benchmark/` est obsolète. Le nouveau venv est `.venv-voxtral-rocm/`. La doctrine bootstrap correspondante de [`benchmark/CLAUDE.md`](../../benchmark/CLAUDE.md) est actualisée par le commit qui accompagne le présent ADR.

Méthodologie : la découverte que le pivot DirectML précédent était basé sur un diagnostic obsolète est un rappel actionnable. Lorsqu'une stack bouge (release majeure, fix amont, deprecated), le **diagnostic vieillit silencieusement** entre le moment où on l'établit et le moment où on agit dessus. La parade documentée pour Deckle : avant un pivot significatif sur une techno en mouvement (PyTorch, transformers, llama.cpp, runtimes d'inférence), vérifier le statut courant des issues GitHub et des releases — c'est précisément ce que les agents recherche du 2026-05-27 ont fait, et qui a débloqué la voie C.
