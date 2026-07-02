---
description: "Recherche vérifiée (deep-research, 2026-07-02) — faisabilité du juge ONNX de l'étage phrase (phase 4) : scoring log-prob possible sur les primitives d'onnxruntime-genai en C#, DirectML seul chemin GPU AMD RDNA3 mais en maintenance, ROCm mort ; le choix du modèle (Luth, capacité FR, débit, shortlist) reste ouvert faute de matière vérifiée."
type: research-report
---

# Juge ONNX de l'étage phrase — le runtime tient, le modèle reste à décider

Commande de Louis (grill du 2026-07-02) : vérifier la faisabilité runtime, sans re-litiger l'architecture décidée — l'étage phrase du correcteur scorera un ensemble fermé de variantes candidates par log-probabilités (candidats bornés, pas de génération libre), in-process en C#/.NET 8+, via ONNX Runtime GenAI (Microsoft.ML.OnnxRuntimeGenAI), sur AMD RX 7900 XT (RDNA3, 20 Go, Windows 11, pas de CUDA). Classes de modèles nommées au plan : Luth (famille fine-tune français) et Qwen3 à l'échelle 0.6B–1.7B. Méthode : harnais deep-research, 5 angles parallèles, 23 sources primaires, 114 claims extraits, les 25 plus porteurs vérifiés par 3 votes adverses chacun — 22 confirmés, 3 réfutés, 7 findings après fusion. Réserves par finding ; le choix final du modèle reste au mainteneur.

## 1. Le chemin de scoring existe — au niveau des primitives

**Le scoring par log-probabilités est constructible sur les primitives d'onnxruntime-genai.** Le Generator accepte l'ajout de séquences de tokens arbitraires (`append_tokens` / `AppendTokens`, distinct de `generate_next_token`, disponible depuis v0.6.0 pour le décodage continu) et expose les logits bruts par pas (`get_logits() -> float32` en Python ; via `GetOutput("logits")` dans les bindings C# livrés). C'est exactement la mécanique requise pour le forced-decoding d'une phrase candidate fixe. Des bindings C#/.NET officiels sont publiés : NuGet Microsoft.ML.OnnxRuntimeGenAI 0.14.1 (2026-06-02, cible .NET 8). [onnxruntime.ai/genai/python, README genai, Generator.cs, guide de migration]

## 2. Pas de mode scoring dédié, et la doc C# est périmée

**Il n'existe aucune API de scoring/teacher-forcing dédiée ni de feature « logprob » annoncée** : les release notes v0.11.2 (nov. 2025) → v0.14.0 (mai 2026) ne mentionnent jamais logits/log-prob/scoring/perplexity. Pire, la référence C# publiée sur onnxruntime.ai est périmée — elle documente encore `ComputeLogits()` (retiré des bindings) et ignore `AppendTokens` / `GetOutput` / `RewindTo` pourtant livrés. Le chemin de scoring doit être vérifié contre la source et le NuGet, pas contre la doc, et l'API entière est marquée « in preview and subject to change ». À noter : `GetLogits`/`SetLogits` n'existent pas dans le binding C# (lecture via `GetOutput("logits")` ; les variantes `get/set_logits` sont côté C API/Python). [onnxruntime.ai/genai/csharp, releases genai, Generator.cs]

## 3. DirectML — seul chemin GPU AMD RDNA3, mais en maintenance déclarée

**DirectML est aujourd'hui le seul chemin GPU réaliste pour la RX 7900 XT** sous onnxruntime-genai sur Windows : les backends listés sont CPU, CUDA, DirectML, TRT-RTX, OpenVINO, QNN, WebGPU (« AMD GPU » seulement « on the roadmap » ; ni ROCm ni Vulkan) ; v0.14.0 livre encore les paquets `win-x64-dml` avec du travail de test DML actif ; et l'EP DirectML couvre tout GPU DX12, AMD GCN 1+ inclus — la 7900 XT (DX12 Ultimate) est compatible. **Mais DirectML est officiellement en maintenance** : bannière « DirectML is in maintenance mode » sur le repo Microsoft, « sustained engineering » côté ONNX Runtime, étiqueté « DirectML (legacy) » dans Windows ML. Il continuera de shipper avec Windows et reste supporté, mais ne recevra que des correctifs sécurité/conformité — le développement est passé à Windows ML. [README genai, releases genai, github.com/microsoft/DirectML, docs EP DirectML, Windows ML]

## 4. ROCm est une voie morte ; Windows ML/MIGraphX bloqué pour GenAI

**ROCm ne mène nulle part pour ce projet** : onnxruntime-genai a retiré le support ROCm en v0.13.0 (avril 2026), l'EP ROCm a été supprimé d'ONNX Runtime depuis la 1.23 (migration pointée vers MIGraphX, lui-même Linux-only), et il n'a de toute façon jamais existé en Windows natif — AMD documente que « the entire ROCm stack is not yet supported on Windows » (seul PyTorch Windows embarque des composants ROCm 7.2.1). De plus, la RX 7900 XT est absente de la matrice de compatibilité Windows Radeon (le XTX y figure, pas le XT) ; elle n'apparaît que dans la matrice HIP SDK, un périmètre différent. [changelog v0.13.0, docs EP ROCm, docs AMD Windows compatibility]

**Le successeur Windows ML est bloqué pour ce cas d'usage** : le catalogue d'EP téléchargeables se limite à MIGraphX (AMD GPU), NvTensorRtRtx, OpenVINO, QNN et VitisAI (AMD NPU) — aucun ROCm, Vulkan ou WebGPU — et Microsoft déclare pour MIGraphX « This execution provider is not supported for GenAI scenarios today », avec en prime un pin de driver GPU exact. Le chemin AMD-GPU « moderne » de Microsoft est donc inutilisable pour onnxruntime-genai aujourd'hui, ce qui renvoie vers DirectML-legacy. [Windows ML supported-execution-providers]

## 5. Modèles — architectures supportées, exports communautaires

**Qwen3 et SmolLM3 sont officiellement convertibles et exécutables** dans onnxruntime-genai : le README liste « Qwen (language + vision) », « SmolLM3 », « Gemma », « Llama », « Mistral » parmi les architectures supportées, et le model builder (`src/python/py/models/builder.py`) contient des branches explicites `Qwen3ForCausalLM` et `SmolLM3ForCausalLM` (plus Gemma/Gemma2/Gemma3, Llama, Mistral). Les familles candidates (Qwen3 0.6B/1.7B) et plusieurs fallbacks ont donc un chemin d'export officiel. Réserve : le support d'architecture ne garantit pas chaque combinaison EP × quantization sur RDNA3. [README genai, builder.py]

**Un export ONNX communautaire de Qwen3-0.6B existe et est utilisé** : `onnx-community/Qwen3-0.6B-ONNX` (org maintenue notamment par Xenova/HF staff), converti de `Qwen/Qwen3-0.6B`, avec huit variantes de quantization vérifiées à l'octet — dont `model_q4f16.onnx` (570 Mo) et `model_int8.onnx` (618 Mo), adaptées à une empreinte locale réduite. Réserve : ces exports ciblent Transformers.js (pas de `genai_config.json` documenté) ; leur chargeabilité directe par Microsoft.ML.OnnxRuntimeGenAI est une question distincte, le chemin sûr restant l'export via le model builder officiel. [huggingface onnx-community/Qwen3-0.6B-ONNX]

## 6. Trou majeur du livrable — la décision modèle reste ouverte

**Aucun claim survivant ne couvre le cœur du choix modèle.** Luth (équipe, modèle de base, tailles, benchmarks français, licence, exportabilité ONNX), les preuves de capacité française de Qwen3/SmolLM3, les licences comparées, et le débit CPU en tokens/s à 0.6B–1.7B int4 ne sont soutenus par aucune matière vérifiée. La « shortlist classée » demandée n'est pas tenable sur cette base. Note : la phase de recherche a bien fait remonter des sources Luth — arXiv 2510.05846 (« Luth: Efficient French Specialization for Small Language Models »), et les dépôts `kurakurai/Luth-1.7B-Instruct` / `kurakurai/Luth-0.6B-Instruct` / `github.com/kurakurai/Luth` — mais aucune n'a produit de claim ayant passé la vérification ; elles servent de point de départ à une passe dédiée, pas de fait établi.

Questions ouvertes restantes :

- **Luth** : qui le produit, sur quelle base, quelles tailles, quelle licence, et son architecture est-elle couverte par le model builder (probablement oui si base Qwen3/Llama, mais rien de vérifié) ?
- **Coût du scoring** : après `AppendTokens` d'une séquence multi-token, le tenseur logits exposé (`GetOutput("logits")` en C#) couvre-t-il toutes les positions ajoutées ou seulement la dernière — c.-à-d. scorer un candidat coûte-t-il un forward ou N forwards ? Impact perf majeur pour scorer N variantes.
- **Chargeabilité des exports communautaires** : les exports onnx-community (Transformers.js, sans `genai_config.json`) se chargent-ils tels quels dans Microsoft.ML.OnnxRuntimeGenAI, ou faut-il repasser par le model builder ? Un test de fumée local trancherait en ~1 h.
- **Débit réel** (tokens/s) pour le scoring de phrases françaises courtes à 0.6B–1.7B int4 sur cette machine : CPU pur, DirectML, et l'EP WebGPU de genai sur RDNA3/Windows (jamais vérifié par les claims).

**Tension non résolue dans la vérification** : trois claims affirmant que les exports onnx-community ne sont PAS directement chargeables par GenAI ont été réfutés 0-3, alors que les vérificateurs des claims confirmés répètent ce même caveat — l'incompatibilité n'est ni prouvée ni infirmée ; le test de fumée local est le seul juge.

**Risque de moyen terme** : DirectML est viable aujourd'hui mais en maintenance déclarée, horizon fonctionnel figé, successeur inutilisable pour GenAI — un squeeze possible du GPU AMD à surveiller. **Sensibilité temporelle forte** : état vérifié au 2026-07-02 sur un écosystème mouvant (releases genai mensuelles, transition Windows ML en cours).

## Sources principales

- Runtime / scoring — README genai : https://github.com/microsoft/onnxruntime-genai ; API Python : https://onnxruntime.ai/docs/genai/api/python.html ; API C# (périmée) : https://onnxruntime.ai/docs/genai/api/csharp.html ; releases : https://github.com/microsoft/onnxruntime-genai/releases ; migration : https://onnxruntime.ai/docs/genai/howto/migrate.html
- Execution providers — DirectML : https://github.com/microsoft/DirectML ; EP DirectML : https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html ; Windows ML : https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers ; ROCm EP : https://onnxruntime.ai/docs/execution-providers/ROCm-ExecutionProvider.html ; AMD Windows : https://rocm.docs.amd.com/projects/radeon-ryzen/en/latest/docs/compatibility/compatibilityrad/windows/windows_compatibility.html
- Modèles — model builder : https://github.com/microsoft/onnxruntime-genai/blob/main/src/python/py/models/builder.py ; Qwen3-0.6B-ONNX : https://huggingface.co/onnx-community/Qwen3-0.6B-ONNX ; Qwen3-1.7B : https://huggingface.co/Qwen/Qwen3-1.7B
- Point de départ passe Luth (non vérifiées) : arXiv 2510.05846 https://arxiv.org/pdf/2510.05846 ; https://huggingface.co/kurakurai/Luth-1.7B-Instruct ; https://huggingface.co/kurakurai/Luth-0.6B-Instruct ; https://github.com/kurakurai/Luth
- Fallbacks (non classés) : SmolLM3 https://huggingface.co/blog/smollm3 ; Gemma-3-1b-it-ONNX https://huggingface.co/onnx-community/gemma-3-1b-it-ONNX
