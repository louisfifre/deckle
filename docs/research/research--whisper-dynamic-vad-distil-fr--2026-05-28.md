---
name: research-whisper-dynamic-vad-distil-fr-2026-05-28
description: "Cartographie de la voie composite « Whisper dynamic windowing + VAD énergie + distil-fr » comme alternative parallèle aux trois POC ASR (V1 Phi-4 OGA, V2 Voxtral ONNX/DirectML, V3 Voxtral Burn/wgpu). État upstream, intégration IAsrBackend, gain/coût estimés, points d'attention propres à la stack Deckle."
type: research
---

# Whisper dynamic + VAD énergie + distil-fr — cartographie pour décision Deckle (état au 2026-05-28)

> Sources : trois sub-agents recherche externe (general-purpose) lancés en session 2026-05-28 — un par composante. Sources triangulées : arXiv 2024-2026, repos GitHub officiels (whisper.cpp, sandrohanea/whisper.net, ufal, ten-framework, libfvad), model cards HuggingFace (bofenghuang, eustlb), papiers distil-whisper et hallucinations Whisper. Méfiance sur les sources antérieures à 2025-06 pour l'écosystème, sur les chiffres comparatifs entre datasets différents (Common Voice 13 vs 17 notamment).

## Context

Trois voies POC ASR sont en cours sur Deckle au 2026-05-28 — V1 Phi-4 OGA (bloquée par doctrine no-Q4 + crash MatMulNBits), V2 Voxtral ONNX/DirectML (bloquée perf par KV cache O(N²)), V3 Voxtral 4B Burn/wgpu/Vulkan (sanity BF16 à faire). Aucune n'a encore convergé en livrable production. La voie présente ici, baptisée informellement « voie 4 », est une alternative à gain quotidien immédiat estimée par Louis à 5-6 jours d'effort : **elle ne change pas le backend ASR, elle améliore la stack actuelle whisper.cpp + Vulkan** sur les trois axes orthogonaux qui composent son nom.

Le verrou actuel du backend Whisper-en-production de Deckle n'est ni le moteur natif (stable, mature, bien intégré via P/Invoke), ni le GPU (RX 7900 XT sur Vulkan bien supportée, cf. [ADR-0008](../adr/0008-rester-sur-vulkan-pour-backends-gpu-natifs.md)). Le verrou est la fenêtre 30 s par défaut du décodeur Whisper, qui impose une latence minimale incompressible sur la dictée hotkey, combinée aux hallucinations sur silence (« Sous-titres réalisés par la communauté d'Amara.org » et variantes) que Louis vit en prod, et au coût mémoire/VRAM de `whisper-large-v3.bin` à 3.1 GB. Les trois composantes de la voie 4 attaquent ces trois symptômes en parallèle.

La fiche [research--whisper-alternatives-fine-windowing--2026-05-27.md](research--whisper-alternatives-fine-windowing--2026-05-27.md) a déjà couvert l'angle « comment réduire la fenêtre 30 s » et tranché en faveur de l'exposition de `audio_ctx` 256-512. Le présent document complète cet ancrage en cartographiant les deux composantes voisines (VAD énergie en amont, distil-fr en remplacement modèle), en creusant ce que « dynamic » signifie au-delà du `audio_ctx` fixe, et en proposant une lecture composée des trois leviers comme système plutôt que comme améliorations isolées.

## Cadrage — ce que cette voie est et n'est pas

**Ce qu'elle est.** Une composition de trois améliorations indépendantes de la stack whisper.cpp existante : (1) **un windowing piloté** par les caractéristiques du signal (durée d'utterance, présence de silences), implémenté soit via `audio_ctx` adaptatif soit via découpe upstream ; (2) **un préprocesseur VAD** qui élimine le silence avant de le donner à Whisper, idéalement maison en C# pur ou via un binding léger ; (3) **un modèle Whisper distillé pour le français** qui réduit l'empreinte VRAM et le temps d'inférence sans bascule d'écosystème. Les trois leviers se combinent : un distil-fr dec2 traite une fenêtre courte plus vite, un VAD énergie en amont coupe le silence avant que Whisper hallucine, un windowing dynamique adapte la fenêtre au contenu.

**Ce qu'elle n'est pas.** Pas un changement de backend ASR — `IAsrBackend` reste `WhisperBackend`, conformément à [ADR-0007](../adr/0007-rester-sur-whisper-cpp-surveiller-voxtral.md) et [ADR-0010](../adr/0010-backend-asr-pluggable-via-iasrbackend.md). Pas un pivot vers un nouveau modèle ASR multimodal ou un nouveau runtime (Voxtral, Phi-4, ONNX/DirectML restent dans leurs POC respectifs). Pas du streaming temps réel pur — la dictée Deckle est hotkey-driven, donc bornée par l'humain au moment du Stop. Pas une refonte de la state machine côté `TranscriptionEngine` — l'orchestrateur reste backend-agnostique et la composition se fait à l'intérieur de `WhisperBackend` plus la chaîne audio.

La voie est explicitement **non concernée par la doctrine no-Q4/INT4 ASR** ([mémoire `project_deckle_asr_quantization_doctrine`](../../C:/Users/Louis/.claude/projects/D--projects-ai-deckle/memory/project_deckle_asr_quantization_doctrine.md)) au sens où elle ne change pas le backend ; en revanche elle hérite de cette doctrine sur la composante distil-fr — seuls les GGML FP16 sont éligibles, les `ggml-model-q5_0.bin` publiés à côté restent écartés.

## Composante A — Whisper dynamic windowing

### A.1 Le levier `audio_ctx` et la formule adaptative

La constante 30 s de Whisper n'est pas un fatalité de whisper.cpp. Le paramètre `whisper_full_params.audio_ctx` permet de réduire la taille de la fenêtre d'encodeur. Côté Deckle, le champ existe déjà dans la struct P/Invoke ([src/Deckle.Transcription.Whisper/Pinvoke/WhisperStructs.cs:49](../../src/Deckle.Transcription.Whisper/Pinvoke/WhisperStructs.cs)) **mais n'est pas mappé** depuis les settings — la valeur passe à zéro et whisper.cpp utilise donc sa fenêtre 30 s pleine par défaut. C'est le levier le moins coûteux à activer : ajouter un champ `EngineSettings.AudioContextSize` (ou équivalent) et étendre `WhisperParamsMapper` pour le router vers la struct native.

L'angle réellement « dynamique » est documenté empiriquement dans [whisper.cpp issue #1855](https://github.com/ggml-org/whisper.cpp/issues/1855) sous forme de formule : `audio_ctx = (audio_length_seconds / 30) × 1500 + 128`, arrondi à un multiple de 64. Sur 200 clips Common Voice de ~5.7 s, la mesure de l'issue rapporte 204 s → 60 s (×3.4 d'accélération) avec WER 20.06 → 19.2 (légère amélioration, pas dégradation). *Établi pour l'anglais Common Voice court, non répliqué pour le français ni pour des durées plus longues, et pas de réponse de mainteneur visible dans le ticket — incertitude marquée explicitement.*

L'application concrète pour Deckle : la durée de l'enregistrement est connue au Stop, donc `audio_ctx` peut être calculé à la volée avant `whisper_full` au lieu d'être réglé statiquement à 256 ou 512. Une utterance de 3 s utiliserait `audio_ctx ≈ 256` (gain ×10 sur l'inférence), une utterance de 25 s repasserait au `audio_ctx ≈ 1408` proche du défaut (donc pas de dégradation sur les longues). C'est le sens littéral de « dynamic » dans le nom de la voie.

### A.2 Point d'attention — répétitions à `audio_ctx` trop bas

Le décodeur Whisper a été entraîné sur des fenêtres de 30 s. À `audio_ctx` très réduit, le pattern documenté est la **répétition infinie de tokens en fin** — le décodeur n'a plus assez de contexte temporel et entre en boucle. La fiche du 2026-05-27 le marquait déjà ; les seuils exacts dépendent du modèle et n'ont pas été chiffrés publiquement. *Doctrine prudente à benchmarker localement aux paliers 128 / 256 / 384 / 512 sur le corpus calibration Deckle avant de pousser plus bas.* L'orchestrateur Deckle dispose déjà d'un `RepetitionDetector` côté backend Whisper ([src/Deckle.Transcription.Whisper/](../../src/Deckle.Transcription.Whisper/CLAUDE.md)) qui couperait l'inférence en cas de boucle — filet de sécurité existant.

### A.3 Approches recherche 2025-2026 — utilisables ou pas

Quatre approches algorithmiquement plus sophistiquées que l'`audio_ctx` adaptatif ont émergé dans la littérature 2024-2026, à classer par utilisabilité pour Deckle.

**[Simul-Whisper (arXiv:2406.10052)](https://arxiv.org/abs/2406.10052)** — politique AlignAtt qui utilise l'attention cross-décodeur pour décider quand arrêter de décoder dans le chunk courant. WER dégradation moyenne **1.46 % à chunk = 1 s**, SOTA streaming sans fine-tuning. *Technique d'inférence pure, pas de retraining, mais pas implémentée dans whisper.cpp à ce jour. Adoptable côté Deckle uniquement si quelqu'un porte la politique en C/C++ dans le décodeur — coût indéterminé.*

**[SimulStreaming (ufal/SimulStreaming)](https://github.com/ufal/SimulStreaming)** — fusion Simul-Whisper + WhisperStreaming, ~5× plus rapide que ce dernier, vainqueur IWSLT 2025. **Backend OpenAI Whisper natif PyTorch**, pas whisper.cpp. Release marquée « Noncommercial version » à vérifier. *Disqualifié de fait pour une intégration native .NET — implique le sous-process Python qu'on cherche à éviter.*

**[WhisperRT / CarelessWhisper (arXiv:2508.12301)](https://arxiv.org/abs/2508.12301)** — rend l'encodeur Whisper causal pour traitement incrémental. **Nécessite un fine-tuning** de l'alignement encodeur-décodeur, donc nouveau modèle à charger. *Hors scope si on veut rester sur les modèles standards HuggingFace.*

**[WhisperPipe (arXiv:2604.25611)](https://arxiv.org/abs/2604.25611)** — architecture streaming avec buffer dynamique et fenêtres recouvrantes. Claims spectaculaires (latence médiane 89 ms, WER à 2 % de l'offline, 48 % moins de mémoire GPU). *Prépublication récente d'un mois, taille de modèle non spécifiée dans l'abstract — hypothèse à valider externalement avant de la prendre pour acquis.*

Lecture composite : aucune de ces approches n'est portable trivialement sur la stack whisper.cpp + .NET de Deckle. L'`audio_ctx` adaptatif + VAD upstream (= ce que cette voie propose) reste donc la meilleure approximation accessible du « dynamic » sans bascule d'écosystème.

### A.4 whisper.cpp en 2026 — leviers natifs disponibles

Latest stable identifié par les sources : **v1.8.4 (mars 2025)**, avec un trou de quatorze mois de releases non documentées dans la passe — *à confirmer directement sur [le repo](https://github.com/ggml-org/whisper.cpp/releases) avant tout chantier d'intégration.*

Les leviers stabilisés à v1.8.4 et pertinents pour cette voie :

- **VAD Silero natif intégré** depuis v1.7.6 (juin 2024), maintenu jusqu'à v1.8.4 (fix VAD time mapping drift). Paramètres CLI `--vad`, `--vad-model`, `--vad-threshold`, `--vad-min-speech-duration-ms`, `--vad-min-silence-duration-ms`, `--vad-speech-pad-ms`. *Exposition via API C dans `whisper.h` non confirmée par les sources consultées — à vérifier en lisant `whisper.h` directement avant de conclure que le VAD Silero natif est appelable depuis un binding P/Invoke.*
- **`--carry-initial-prompt`** depuis v1.8.1 — transporte le prompt initial à travers tous les chunks. Levier direct pour reconstituer du contexte cross-chunk en mode windowing court. Déjà exposé côté Deckle dans `EngineSettings.CarryInitialPrompt = true`.
- **Mitigations hallucination par paramètres** : `suppress_blank`, `suppress_nst`, `suppress_regex` (filtrage des hallucinations connues type « thanks for watching »), `no_speech_threshold` et `logprob_threshold` pour rejeter les chunks à faible confiance, `entropy_thold` pour activer le fallback température. Tous exposés dans `WhisperFullParams` et déjà mappés côté `WhisperParamsMapper`.
- **Beam size — contre-intuitif.** Plusieurs sources convergent : beam élevé amplifie les hallucinations en privilégiant les séquences hautes-probabilité même fausses. *Hypothèse à mesurer sur le corpus Deckle avant de changer le défaut `BeamSize = 5`.*

### A.5 Whisper.net (sandrohanea) — état au v1.9.0 (novembre 2025)

`AudioContextSize` est exposé côté `WhisperProcessorOptions` ([source confirmée sur le repo](https://github.com/sandrohanea/whisper.net/blob/main/Whisper.net/WhisperProcessor.cs)). L'accès via builder fluent n'est pas confirmé par les sources — option vraisemblablement accessible via le champ d'options mais pas nécessairement via une méthode `WithAudioContextSize(...)`. *Non bloquant pour Deckle : on utilise déjà un wrapper P/Invoke maison ([src/Deckle.Transcription.Whisper/Pinvoke/](../../src/Deckle.Transcription.Whisper/CLAUDE.md)) qui expose `whisper_full_params` au complet — `audio_ctx` est dans la struct, il manque juste le mapping settings → struct. Whisper.net n'apporte rien que le wrapper maison ne puisse faire.*

Manque côté Whisper.net comme côté wrapper maison : pas d'API streaming chunké avec contexte propagé natif. Le pattern à implémenter côté Deckle reste : VAD upstream → découpe en utterances → un `TranscribeAsync` par utterance avec `Prompt` portant la dernière phrase pour le contexte, `audio_ctx` dimensionné à la durée. Architecture compatible avec l'`IAsrBackend.TranscribeAsync` actuel — la coupe en utterances se ferait en amont de l'appel backend.

## Composante B — VAD énergie

### B.1 Algorithmique et calibrage 2026

Le noyau du VAD énergie est resté stable depuis vingt ans, confirmé par les sources 2024-2026 sans remise en cause de fond. Frames de **10 à 30 ms** (20 ms canonique chez WebRTC et libfvad), deux features par frame : **RMS** (root mean square, indicateur d'amplitude) et **ZCR** (zero-crossing rate, sépare voix harmonique du bruit blanc du silence). Le couplage RMS+ZCR est ce que [`rymshasaeed/Voice-Activity-Detection`](https://github.com/rymshasaeed/Voice-Activity-Detection) implémente en ~200 lignes Python — extrapolé à du C# avec tests, machine à états et hangover, on est dans l'ordre **300-500 lignes** pour une implémentation sérieuse, possiblement jusqu'à 800-1000 avec gestion des edge cases (clipping, DC offset, frames partielles).

Le seuillage adaptatif est l'état de fait. La formule canonique ([VOCAL](https://vocal.com/voice-quality-enhancement/voice-activity-detection-with-adaptive-thresholding/), [paper IAENG](https://www.iaeng.org/IJCS/issues_v36/issue_4/IJCS_36_4_16.pdf)) : EMA du noise floor sur les frames classées non-speech, `noise_estimate[n] = α · noise_estimate[n-1] + (1-α) · frame_energy[n]`, threshold = `k · noise_estimate`. Valeurs typiques : `α ∈ [0.95, 0.99]` pour une constante de temps 200 ms-1 s à 50 Hz de frames, `k ∈ [2, 5]` (le paper IAENG cite 3×). **Le seuil n'est jamais fixé en dB SPL absolus** — toutes les sources convergent sur le ratio relatif à un noise floor estimé en ligne, ce qui dispense de calibrer le matériel.

Filtre band-pass préalable recommandé par toutes les sources sérieuses : limiter l'énergie à **80 Hz - 4 kHz** (voix conversationnelle) ou 8 kHz si on garde les fricatives. Sans ce filtre, ronflement secteur 50 Hz et souffle ventilateur >5 kHz polluent la mesure. Antipattern fréquent dans les tutoriels web.

### B.2 Énergie vs WebRTC vs Silero — chiffres comparatifs

L'écart de robustesse au bruit est le critère qui sépare les approches, pas la latence.

**Latence brute.** Énergie en microsecondes par frame, Silero en sous-milliseconde sur CPU AMD ([Picovoice benchmark](https://picovoice.ai/docs/benchmark/vad/), [Stackademic](https://blog.stackademic.com/silero-vad-the-lightweight-high-precision-voice-activity-detector-26889a862636) — RTF 0.0043). Imperceptible à l'échelle d'une hotkey de dictée — l'écart de latence brute ne décide pas.

**Decision lag.** [Le README de TEN VAD](https://github.com/TEN-framework/ten-vad) documente plusieurs centaines de millisecondes de retard côté Silero pour détecter les transitions speech→non-speech, dû à la structure neuronale qui regarde un contexte temporel. *Distingue throughput par frame (Silero est rapide) du lag de décision (Silero est lent à déclarer une fin de speech).* Pour la dictée hotkey, ce lag de fin se traduit par une pause perçue entre « j'ai fini de parler » et le déclenchement du paste — pénalité ergonomique réelle.

**Robustesse au bruit non-speech sur préprocessing Whisper.** Chiffre central : [arXiv 2501.11378](https://arxiv.org/html/2501.11378v1) (« Investigation of Whisper ASR Hallucinations Induced by Non-Speech Audio », 2025) mesure que **WebRTC laisse passer 12.5-15.4 % d'hallucinations Whisper, Silero en laisse passer 0.2 %**, avec un WER respectif de 68-75 % vs 8-11 %. Un VAD énergie pur ferait moins bien que WebRTC (qui inclut un GMM 6 bandes). L'ordre établi : **énergie < WebRTC < Silero < TEN VAD**.

*L'amplitude du facteur 75× (15% vs 0.2%) entre WebRTC et Silero est spectaculaire et mérite réplication indépendante avant d'être pris pour acquis. Cohérent avec les benchmarks de précision, mais à confirmer.*

### B.3 Le cas hotkey-driven Deckle — dégradation du problème

La dictée Deckle n'est pas le worst case du VAD. **L'utilisateur déclenche manuellement** par hotkey et relâche manuellement — donc la fenêtre temporelle est déjà bornée par l'humain. Le VAD n'a pas à classifier finement dans un flux continu de plusieurs minutes ; son rôle se réduit à **trimmer les silences en début/fin de l'enregistrement hotkey** et éventuellement à détecter les pauses internes pour les passer en chunks séparés à Whisper.

Dans ce scénario dégradé, **un VAD énergie suffit largement** : le risque de manquer une transition courte en milieu de flux disparaît, la sensibilité au bruit ambiant continu (café, voisin) devient moins critique parce que l'humain n'appuie pas sur la hotkey en plein milieu d'un brouhaha non-intentionnel. Le différentiel de qualité Silero vs énergie se rapproche sensiblement de zéro dans ce cas d'usage. *Hypothèse non chiffrée publiquement — aucune source documente un système de dictée moderne 2024-2026 reposant uniquement sur un VAD énergie en hotkey-driven. Pas d'invalidation non plus.*

### B.4 Implémentations utilisables côté .NET

| Candidat | Type | Verdict | Notes |
|---|---|---|---|
| [WebRtcVadSharp](https://github.com/ladenedge/WebRtcVadSharp) | P/Invoke .NET sur DLL native WebRTC | Production, **maintenance morte** depuis avril 2022 | API `HasSpeech(byte[], SampleRate, FrameLength)`, 4 modes d'agressivité. Code WebRTC sous-jacent stable. |
| [WebRtcVad.NET](https://libraries.io/nuget/WebRtcVad.NET) | Port managed C# pur de l'algo WebRTC | Expérimental — à valider | Zéro dépendance native, .NET 8+. Pas d'audit public sur la fidélité de portage. |
| [libfvad](https://github.com/dpirch/libfvad) | C natif standalone (fork WebRTC VAD) | Production | API documentée, modes 0-3. Nécessite P/Invoke + build natif Windows. |
| [TEN VAD](https://github.com/TEN-framework/ten-vad) | VAD neuronal ONNX | Expérimental à surveiller | 2025, annonce RTF 32 % inférieure à Silero. Pas de binding C# officiel, intégrable via ONNX Runtime. |
| Code maison NAudio + RMS+ZCR | C# pur, dépendance NAudio existante | À écrire | ~300-500 lignes, jusqu'à 800-1000 avec tests et edge cases. Aligné philosophie .NET native. |
| [Calm-Whisper (arXiv:2505.12969)](https://arxiv.org/html/2505.12969v1) | Modification attention Whisper, pas VAD | Recherche 2025 | Approche orthogonale — neutraliser les têtes d'attention qui hallucinent au lieu de pré-filtrer. |

L'écosystème NAudio est déjà connu côté `Deckle.Audio` ([src/Deckle.Audio/CLAUDE.md](../../src/Deckle.Audio/CLAUDE.md)) — l'écriture d'un `EnergyVad` comme `ISampleProvider` consommant PCM 16 kHz mono, calculant RMS+ZCR par frame de 320 samples (= 20 ms), maintenant l'EMA du noise floor, appliquant l'hangover et émettant des événements `SpeechStarted`/`SpeechEnded` est un chantier circonscrit qui s'inscrit naturellement dans le module.

### B.5 Pièges spécifiques à la dictée

**Hangover et padding pre-speech.** Valeurs documentées convergentes : hangover (fin de parole) **200-500 ms** (WhisperX et faster-whisper utilisent typiquement 300 ms), padding pre-speech **100-300 ms** via buffer circulaire pour récupérer le début manqué, min-silence-duration **200-500 ms** pour ne pas fragmenter sur micro-pauses inter-mots. Whisper.cpp expose `--vad-speech-pad-ms` exactement pour ce padding pre-speech — *si on bascule sur le VAD Silero natif, les paramètres sont déjà alignés sur les valeurs de la doctrine.*

**Voix calmes / aiguës / nasales.** Aucune source ne documente de problème spécifique côté Silero ou WebRTC, mais le VAD énergie y est intrinsèquement plus sensible — voix murmurée = faible énergie = risque de classification silence. Parade unique : baisser le facteur de seuil `k` (de 3× à 2×) au prix de plus de faux positifs sur le bruit. **Trade-off non résolvable algorithmiquement** — à accepter, ou à basculer sur un VAD modèle.

**Faux positifs sur transitoires.** Clavier mécanique, clic souris, expiration sur le micro. RMS+ZCR ne les filtre pas suffisamment. Heuristique documentée : **exiger N frames consécutives au-dessus du seuil** (typiquement 3-5 frames = 60-100 ms) avant de déclarer speech. Délai de déclenchement de 60-100 ms en contrepartie.

**Auto-calibration.** Sur les 300-500 premières millisecondes après l'appui hotkey on suppose le signal = silence et on initialise le noise floor. Si l'utilisateur commence à parler immédiatement, noise floor trop haut → VAD passe tout → dégradation silencieuse. Variante plus robuste : calibration continue en arrière-plan, qui suppose une capture micro permanente — *incompatible avec la doctrine vie privée probable de Deckle, à arbitrer.*

### B.6 Couplage avec Whisper en aval

[arXiv 2501.11378](https://arxiv.org/html/2501.11378v1) chiffre que **40.3 % des inférences Whisper sur non-speech génèrent du texte halluciné**, avec « thank you » (24.76 %) et « thanks for watching » (10.32 %) en tête. En français, l'équivalent observé en prod Deckle est « Sous-titres réalisés par la communauté d'Amara.org ». **Le VAD upstream est aujourd'hui la mitigation la plus efficace documentée**, devant baisse de température, augmentation du beam size ou pénalités de répétition.

[whisper.cpp issue #1724](https://github.com/ggml-org/whisper.cpp/issues/1724) sur les hallucinations silence reste **ouverte sans résolution** — la solution communautaire est précisément le VAD upstream, comme le fait WhisperX.

Limitation intrinsèque : même Silero VAD v5 atteint seulement 61 % d'utterance-level accuracy sur ESC-50 (sons environnementaux non-speech) — jusqu'à 40 % du bruit pur peut être classifié speech. *Donc même un VAD parfait ne suffit pas — l'approche Calm-Whisper qui traite le problème côté modèle reste pertinente comme deuxième ligne de défense long terme.*

## Composante C — distil-fr

### C.1 Inventaire 2025

Trois lignées vivantes identifiées, toutes sous licence MIT, toutes téléchargeables sans authentification HuggingFace.

| Famille | Variantes | Date | Teacher |
|---|---|---|---|
| [bofenghuang v0.1](https://huggingface.co/bofenghuang) | dec16, dec8, dec4, dec2 | novembre 2024 | `bofenghuang/whisper-large-v3-french` (fine-tune FR du large-v3) |
| [bofenghuang v0.2](https://huggingface.co/bofenghuang/whisper-large-v3-distil-fr-v0.2) | dec2 seul | mars 2025 | `openai/whisper-large-v3` directement |
| [eustlb distil-large-v3-fr](https://huggingface.co/eustlb/distil-large-v3-fr) | dec2 | mars 2025 | `openai/whisper-large-v3` |

Le passage v0.1 → v0.2 chez bofenghuang introduit deux changements substantiels : entraînement étendu sur segments de 30 s (pour préserver explicitement la capacité long-form) et corpus gonflé de ~2 500 heures à 10 402 heures filtrées. *Mais pas de variante dec4/8/16 en v0.2 — uniquement dec2 — donc si on veut un compromis qualité/vitesse intermédiaire, on reste sur v0.1.*

eustlb est un membre HF Staff ; le statut de la lignée est ambigu (pas de model card sur le repo GGML compagnon, pas d'annonce officielle dans la collection distil-whisper de HF). *Possiblement effort interne expérimental publié sans intention de support long terme — à traiter comme référence comparative plutôt que cible production.*

bofenghuang est actif jusqu'à mars 2025 avec extension à d'autres langues (italien v0.2 publié simultanément), pas d'indice d'abandon mais un seul mainteneur identifié, bus factor 1. **Risque orphelin modéré sur bofenghuang, élevé sur eustlb.**

### C.2 Benchmarks WER FR — chiffres et caveat de commensurabilité

| Modèle | Common Voice | MLS FR | VoxPopuli FR | FLEURS FR | Source |
|---|---|---|---|---|---|
| `whisper-large-v3` | 11.04 (CV17) | 4.76 | — | 5.62 | eustlb card |
| bofenghuang dec16 (v0.1) | 7.18 (CV13) | 3.57 | 8.76 | 5.03 | dec16 card |
| bofenghuang dec2 (v0.1) | 9.01 (CV13) | 4.64 | 9.76 | 7.08 | dec2 card |
| eustlb distil-large-v3-fr | 12.68 (CV17) | 5.87 | — | 7.99 | eustlb card |
| bofenghuang distil-fr-v0.2 | non publié dans card | — | — | — | charts en Drive externe |

Lecture critique. La dec16 bofenghuang sort des chiffres **inférieurs au teacher** sur trois datasets (7.18 vs 11.04 sur Common Voice). Contre-intuitif mais explicable : dec16 utilise comme teacher `bofenghuang/whisper-large-v3-french` (fine-tune FR du large-v3 OpenAI), pas `openai/whisper-large-v3` directement. La distillation propage donc en partie l'expertise FR du fine-tune intermédiaire. Le papier distil-whisper rapporte aussi que les distillations à WER filter peuvent dépasser le teacher quand le teacher hallucine sur le test set.

**Caveat majeur : Common Voice 13 vs Common Voice 17.** bofenghuang évalue sur CV13, eustlb sur CV17. **Les benchmarks ne sont pas commensurables directement** — CV17 est plus exigeant que CV13 (plus de locuteurs, plus de bruit), donc comparer bofenghuang dec2 (9.01 sur CV13) à eustlb dec2 (12.68 sur CV17) est trompeur. *Pour arbitrer entre les deux familles il faut repasser sur un corpus unique — idéalement le corpus normalisé Deckle décrit dans [ADR-0011](../adr/0011-corpus-normalise-comme-dataset-ml.md).*

Gain de vitesse : eustlb annonce 5.9× et bofenghuang dec2 5.8× plus rapide que `whisper-large-v3`, jusqu'à 9× avec chunked long-form. Le gain est **préservé sur FR par rapport au mainline anglais**. Pour dec8 et dec16, le gain est intermédiaire mais non chiffré explicitement.

### C.3 Formats GGML disponibles et doctrine no-Q4

Tous les repos bofenghuang publient deux fichiers GGML : `ggml-model.bin` et `ggml-model-q5_0.bin`. **Le `q5_0.bin` est écarté par doctrine** ([mémoire `project_deckle_asr_quantization_doctrine`](../../C:/Users/Louis/.claude/projects/D--projects-ai-deckle/memory/project_deckle_asr_quantization_doctrine.md), candidate ADR-0017 — Q5 est inclus dans le périmètre « no-Q4/INT4 ASR » au sens large, car Q5_0 est plus agressif que Q5_K et a un comportement de dégradation FR analogue selon l'étude Cohere [arXiv 2407.03211](https://arxiv.org/abs/2407.03211)). *À confirmer dans la rédaction de l'ADR si Louis veut formaliser que la doctrine couvre Q5_0 ou seulement Q4/INT4.*

La précision réelle du `ggml-model.bin` se déduit du calcul : pour dec2 à 0.8B paramètres et 1.52 GB de fichier, on retombe sur ~2 octets par paramètre, donc **FP16**. Pour dec16 à ~1.1B et 2.25 GB, idem FP16. *Confirmation indirecte par homologie via [la discussion sur le repo officiel distil-large-v3-ggml](https://huggingface.co/distil-whisper/distil-large-v3-ggml/discussions/1) où sanchit-gandhi (auteur du papier distil-whisper) confirme que le `ggml-model.bin` mainline est FP16. Confirmation explicite côté model cards bofenghuang absente — à vérifier via `whisper_print_system_info` après chargement.*

Le repo `eustlb/distil-large-v3-fr-ggml` est anormal : deux fichiers `ggml-distil-large-v3-fr.bin` (1.52 GB) et `ggml-distil-large-v3-fr.fp32.bin` (1.52 GB) tous deux à la **même taille pour un modèle 756M**. Pour 756M paramètres, F32 devrait peser ~3 GB et F16 ~1.5 GB. Probable bug d'étiquetage ou les deux fichiers pointent vers le même blob. *À vérifier en téléchargement effectif si on emprunte cette piste.*

### C.4 Limite chunked long-form et impact court

[Le README officiel whisper.cpp](https://github.com/ggml-org/whisper.cpp) précise : « the chunk-based transcription strategy is not implemented, so there can be sub-optimal quality when using the distilled models with whisper.cpp ». Pour Deckle, **l'enjeu est neutre voire favorable** : la dictée se fait sur fenêtres courtes (< 30 s typique d'un hotkey), donc on tombe sur le mode sequential par défaut, qui n'est pas pénalisant en short-form. Le risque sub-optimal annoncé concerne le long-form > 30 s — hors scope dictée. La v0.2 bofenghuang a été entraînée explicitement pour préserver le sequential long-form, ce qui pourrait introduire un léger surcoût qualité en short-form — *non documenté, à vérifier empiriquement.*

### C.5 Hallucinations short-form en FR — incertitude centrale

Sur le long-form, le papier distil-whisper quantifie **1.3× moins de répétitions 5-grammes et -2.1 % d'erreur d'insertion** vs Whisper teacher. Le model card bofenghuang dec16 confirme « >10% of data filtered out to remove mismatches, poor segmentation, and missing words, significantly reducing hallucinations ». Le mécanisme principal : filtrage par WER heuristique des pseudo-labels d'entraînement, qui écarte les transcriptions hallucinées du teacher.

Sur le short-form FR (dictée Deckle), **aucune source dédiée ne mesure l'hallucination séparément**. Les patterns typiques (« Sous-titres réalisés par la communauté d'Amara.org », « Merci d'avoir regardé cette vidéo », « © Sous-titres ») viennent du corpus pré-entraîné OpenAI, qui ne disparaît pas lors de la distillation française — **les poids encodeur sont gelés et le décodeur distillé apprend uniquement à reproduire les sorties du teacher, donc reproduit potentiellement les mêmes biais**. *Hypothèse à vérifier empiriquement sur le corpus Deckle — c'est le point d'incertitude principal qui détermine si distil-fr fait gagner ou non sur le critère prioritaire de Louis (élimination des hallucinations sur silence).*

### C.6 Provisioning et empreinte

Le pipeline de provisioning Deckle suit déjà le pattern `{repo}/resolve/{branch}/{filename}` pour `ggml-large-v3.bin`. Mappage trivial pour les distil-fr.

| Cible | URL canonique | Taille | VRAM ~ FP16 |
|---|---|---|---|
| dec2 v0.1 | `bofenghuang/whisper-large-v3-french-distil-dec2/resolve/main/ggml-model.bin` | 1.52 GB | ~1.6 GB |
| dec16 v0.1 | `bofenghuang/whisper-large-v3-french-distil-dec16/resolve/main/ggml-model.bin` | 2.25 GB | ~2.3 GB |
| distil-fr v0.2 | `bofenghuang/whisper-large-v3-distil-fr-v0.2/resolve/main/ggml-model.bin` | 1.52 GB | ~1.6 GB |
| eustlb | `eustlb/distil-large-v3-fr-ggml/resolve/main/ggml-distil-large-v3-fr.bin` | 1.52 GB | ~1.6 GB (précision à confirmer) |

Référence : `ggml-large-v3.bin` actuel ~3.1 GB. Donc passage à dec2 → **division par 2** de l'empreinte disque et VRAM, passage à dec16 → ~30 % d'économie. Sur RX 7900 XT à 20 GiB VRAM, l'économie ne libère pas un workstream bloqué côté Whisper — mais elle compte si Deckle co-héberge plus tard un LLM de réécriture local sur la même VRAM.

**Pas de checksum SHA256 publié** dans les model cards. Vérification d'intégrité au premier download à pinner localement (pattern déjà en place pour large-v3 selon [docs/reference/reference--native-runtime--1.0.md](../reference/reference--native-runtime--1.0.md)).

## Composition des trois leviers — pourquoi ensemble

Les trois composantes ne sont pas indépendantes au sens de la qualité finale, malgré leur indépendance d'implémentation. Trois interactions à signaler.

**Dynamic windowing × VAD énergie.** Sans VAD upstream, réduire `audio_ctx` augmente la probabilité d'hallucination sur silence — chaque chunk court est une nouvelle chance de produire un « thank you for watching ». Avec VAD upstream, le silence n'arrive jamais au décodeur Whisper et `audio_ctx` court devient moins risqué. **Le VAD est ce qui rend le dynamic windowing tolérable.**

**Dynamic windowing × distil-fr.** Le décodeur distillé (dec2) est plus rapide donc tolère mieux les fenêtres courtes répétées en série. Le gain ×5.8 sur la dec2 est composable avec le gain ×3-10 de l'`audio_ctx` adaptatif — *ordres de grandeur non multiplicatifs naïvement (les deux gains touchent des phases différentes de l'inférence), mais cumulables non négligeables.*

**VAD énergie × distil-fr.** Les biais hallucinatoires de distil-fr (hérités du teacher OpenAI via le décodeur distillé) sont les mêmes que ceux de `whisper-large-v3` — donc le VAD upstream est aussi nécessaire avec dec2 qu'avec large-v3. *Pas de synergie particulière mais pas d'incompatibilité non plus.*

Lecture composée : **les trois leviers attaquent trois phases différentes du pipeline** (filtrage du signal en amont, taille de fenêtre passée à l'encodeur, modèle d'inférence) et leurs gains s'ajoutent sans s'annuler. C'est ce qui justifie de les considérer comme un système plutôt que comme améliorations isolées.

## Intégration concrète à l'architecture IAsrBackend

L'architecture parent/enfants posée par [ADR-0010](../adr/0010-backend-asr-pluggable-via-iasrbackend.md) impose une discipline : le parent `Deckle.Transcription` reste backend-agnostique, l'enfant `Deckle.Transcription.Whisper` porte les spécificités. Trois zones de modification se dessinent.

**Dans `Deckle.Transcription` (parent).** Le contrat `IAsrBackend` reste inchangé. Le POCO `TranscriptionSettings` ([src/Deckle.Transcription/TranscriptionSettings.cs](../../src/Deckle.Transcription/TranscriptionSettings.cs)) gagne un nouveau bloc `EnergyVadSettings` (ou un mode `Mode = Off | Silero | Energy` sur `SpeechDetectionSettings`) — *à décider à la rédaction, mais le bloc reste backend-agnostique, donc bien à sa place côté parent.* Le pipeline `TranscriptionEngine` lit déjà `Record() → float[]` puis appelle `_backend.TranscribeAsync(...)` — la découpe en utterances par VAD énergie se ferait en amont de l'appel backend, dans l'orchestrateur, **avec un appel `TranscribeAsync` par utterance** plutôt qu'un seul appel pour tout l'audio. Conserve le contrat `IAsrBackend` actuel. Le `segmentSink` réémet les segments dans l'ordre, le bridge `ITranscriptionEngineHost` ne bouge pas.

**Dans `Deckle.Transcription.Whisper` (enfant).** Trois changements internes : (1) ajout du champ `EngineSettings.AudioContextSize` (ou calcul automatique depuis la durée d'utterance) et mapping vers `whisper_full_params.audio_ctx` dans `WhisperParamsMapper` ([src/Deckle.Transcription.Whisper/Engine/WhisperParamsMapper.cs](../../src/Deckle.Transcription.Whisper/Engine/WhisperParamsMapper.cs)) ; (2) extension du catalogue `SpeechModels` pour inclure les distil-fr (dec2 v0.1, dec16 v0.1, distil-fr v0.2) avec URLs HF et tailles ; (3) vérification que la version `libwhisper.dll` embarquée supporte les distil-Whisper au niveau attendu — *non garanti, dépend du tag whisper.cpp pris à la dernière recompile native ([docs/reference/reference--native-runtime--1.0.md](../reference/reference--native-runtime--1.0.md)).*

**Dans `Deckle.Audio` (transverse).** Implémentation de l'`EnergyVad` comme `ISampleProvider` consumant le PCM 16 kHz mono émis par `Record()`. Émission d'événements `SpeechStarted` / `SpeechEnded` avec timestamps. Le module Audio est déjà ouvert aux capacités vocales (cf. [src/Deckle.Audio/CLAUDE.md](../../src/Deckle.Audio/CLAUDE.md)) — la couche VAD énergie s'y inscrit naturellement, distincte du VAD Silero qui vit côté natif whisper.cpp.

**Dans `Deckle.Setup` (provisioning).** Extension du wizard premier lancement pour proposer le modèle distil-fr en alternative à `ggml-large-v3.bin`. Pattern de téléchargement déjà en place, ajout d'URL et de SHA pinned (calculé au premier download local et stocké).

**Surface UI Settings.** La `WhisperPage` actuelle est whisper-centric et expose déjà Silero VAD et beam search. Ajouts attendus : sélecteur de modèle (large-v3 / distil-fr dec2 / distil-fr dec16), toggle VAD mode (Off / Silero natif / Energy maison), slider `audio_ctx` ou option « auto (adaptive) ». *Reste cohérent avec la doctrine `deckle-settings-ux` — progressive disclosure, sensible defaults, settings vs commands.*

## Coût et gain estimés

L'estimation de Louis (5-6 jours) couvre les trois composantes ensemble. Décomposition par composante, ordres de grandeur indicatifs uniquement.

**Composante A — dynamic windowing.** Mapping `audio_ctx` (fixe ou adaptatif) ≈ **0.5 jour** côté backend Whisper (un champ settings, un mapping mapper, un test). Adaptation pipeline pour appeler `TranscribeAsync` par utterance plutôt qu'en bloc ≈ **1-2 jours** côté orchestrateur. Benchmark seuils 128/256/384/512 sur corpus calibration ≈ **0.5-1 jour** sur le banc benchmark existant ([benchmark/CLAUDE.md](../../benchmark/CLAUDE.md)).

**Composante B — VAD énergie.** Si choix code maison NAudio : **2-3 jours** (algorithme + machine à états + tests + intégration `Deckle.Audio`). Si choix librairie tierce (WebRtcVad.NET ou libfvad) : **1-1.5 jour** (binding + tests + tuning hangover/padding). Plus **0.5 jour** d'intégration au pipeline `TranscriptionEngine` pour découper le float[] en utterances.

**Composante C — distil-fr.** Provisioning catalogue + URL + UI sélecteur ≈ **0.5 jour**. Vérification version `libwhisper.dll` et éventuelle recompile si nécessaire ≈ **0.5-2 jours** (dépend si la recompile native passe directement ou si tag whisper.cpp à updater). Benchmark dec2 / dec16 vs large-v3 sur corpus Deckle ≈ **0.5-1 jour**.

**Total ordre de grandeur : 6-10 jours d'effort** pour la voie complète, dont environ 50 % d'implémentation et 50 % de benchmark et tuning. L'estimation Louis à 5-6 jours est dans la fourchette basse — atteignable si on coupe sur le code maison VAD (au profit d'une librairie tierce) et si la recompile native passe directement.

**Gain estimé.** Latence d'inférence : combinaison `audio_ctx` adaptatif (×3-10 selon durée) et dec2 (×5.8 sur large-v3) donne théoriquement un gain composé d'ordre **×10-30 sur les utterances courtes** par rapport au state actuel (large-v3 fenêtre 30 s pleine). *Borne haute optimiste — sera limité par la phase encodeur fixe et par les overheads d'appel répété à `TranscribeAsync`.* Élimination des hallucinations sur silence : qualitative, dépend de la justesse du VAD upstream — *à mesurer sur le corpus Deckle.* Empreinte VRAM : -50 % en dec2.

## Points d'attention propres à la stack Deckle

**Vulkan AMD ([ADR-0008](../adr/0008-rester-sur-vulkan-pour-backends-gpu-natifs.md)).** Aucune composante n'oblige à changer de backend GPU — whisper.cpp + Vulkan reste la stack. Les distil-fr en GGML FP16 chargent sur Vulkan exactement comme `large-v3` (le format encode juste les tenseurs, le nombre de couches du décodeur est un paramètre, pas une rupture d'architecture). *Risque résiduel à signaler : [issue whisper.cpp #2867](https://github.com/ggml-org/whisper.cpp/issues/2867) sur whisper-stream Vulkan AMD 7900 XT Windows reste ouverte ; cible un binaire spécifique mais ne préjuge pas du comportement du lib core sur dictée hotkey — à confirmer empiriquement avant tout chantier.*

**Doctrine no-Q4/INT4 ASR ([mémoire `project_deckle_asr_quantization_doctrine`](../../C:/Users/Louis/.claude/projects/D--projects-ai-deckle/memory/project_deckle_asr_quantization_doctrine.md)).** Stricte : seuls les `ggml-model.bin` FP16 sont éligibles côté distil-fr, les `ggml-model-q5_0.bin` publiés à côté sont écartés. *Question ouverte à trancher dans la rédaction de l'ADR-0017 : la doctrine couvre-t-elle Q5_0 explicitement, ou seulement Q4/INT4 ? L'étude Cohere arXiv 2407.03211 cite FP16→4-bit, donc strictement Q5_0 n'est pas couvert. Mais par homologie de comportement (dégradation FR invisible aux métriques automatiques), inclure Q5_0 par prudence est défendable.*

**Provisioning natif ([docs/reference/reference--native-runtime--1.0.md](../reference/reference--native-runtime--1.0.md)).** La compatibilité distil-Whisper côté lib native dépend du tag whisper.cpp pris au dernier rebuild. Support initial fonctionnel depuis fin 2023 ([issue #1423](https://github.com/ggml-org/whisper.cpp/issues/1423)), donc à priori couvert par toute version raisonnablement récente. *À vérifier directement sur la version embarquée actuellement dans le bundle natif Deckle avant de promettre quoi que ce soit.*

**Observabilité EventSource ([src/Deckle.Diagnostics/CLAUDE.md](../../src/Deckle.Diagnostics/CLAUDE.md)).** Les trois composantes introduisent de nouveaux observables à instrumenter — décisions VAD (frame energy, threshold, state transitions), `audio_ctx` calculé par utterance, durée d'inférence par chunk distil-fr vs large-v3, présence/absence d'hallucinations résiduelles. *À cartographier au moment de l'implémentation via skill `deckle-logging` pour ne pas multiplier les events ad hoc.*

**Articulation avec les trois POC.** La voie 4 n'éteint pas les POC V1/V2/V3. Elle peut être livrée en parallèle (gain quotidien immédiat sur Whisper, sans bloquer les POC) ou comme repli si V1/V2/V3 ne convergent pas. *Décision de priorisation et de séquence à arbitrer par Louis — la cartographie ici ne tranche pas.*

## Risques et incertitudes à lever empiriquement

Six points où l'arbitrage demande une mesure terrain plutôt qu'une recherche externe supplémentaire.

1. **Seuil de dégradation à `audio_ctx` faible sur français.** Documenté qualitativement (répétitions de tokens) mais non chiffré publiquement, et toute la mesure publique est anglaise. À benchmarker localement aux paliers 128 / 256 / 384 / 512 sur le corpus Deckle.

2. **Précision réelle des `ggml-model.bin` bofenghuang.** Déduite à FP16 par calcul de taille, jamais confirmée explicitement dans un model card. Vérifier via `whisper_print_system_info` après chargement.

3. **Hallucinations short-form FR sur distil-fr dec2.** Le profil hallucinatoire short-form n'est pas séparé des chiffres long-form dans les papiers, et le mécanisme structurel (décodeur autoregressif sur encodeur OpenAI gelé) suggère qu'il subsiste. Mesurer sur le corpus Deckle en condition réelle (silence, bruit clavier, micro casque).

4. **Commensurabilité bofenghuang vs eustlb.** CV13 vs CV17, corpus d'évaluation différents — impossible de conclure « X bat Y » sans repasser les modèles sur un corpus unique, idéalement le corpus normalisé Deckle ([ADR-0011](../adr/0011-corpus-normalise-comme-dataset-ml.md)).

5. **Comportement VAD énergie en condition dictée Deckle.** Aucune source 2024-2026 documente un système de dictée reposant uniquement sur VAD énergie en hotkey-driven. Pas d'invalidation publique non plus — territoire à explorer empiriquement.

6. **Version `libwhisper.dll` embarquée et support distil-Whisper.** À confirmer sur le bundle natif actuel avant de baser un livrable dessus. Recompile éventuelle en suivant la recette de [reference--native-runtime--1.0.md](../reference/reference--native-runtime--1.0.md).

## Sources

### Composante A — Whisper dynamic windowing

- [arXiv:2406.10052 — Simul-Whisper](https://arxiv.org/abs/2406.10052)
- [arXiv:2508.12301 — WhisperRT / CarelessWhisper](https://arxiv.org/abs/2508.12301)
- [arXiv:2604.25611 — WhisperPipe](https://arxiv.org/abs/2604.25611)
- [arXiv:2307.14743 — whisper_streaming (LocalAgreement)](https://arxiv.org/pdf/2307.14743)
- [arXiv:2506.17077 — CUNI IWSLT 2025 (SimulStreaming)](https://arxiv.org/html/2506.17077)
- [ufal/whisper_streaming](https://github.com/ufal/whisper_streaming)
- [ufal/SimulStreaming](https://github.com/ufal/SimulStreaming)
- [collabora/WhisperLive](https://github.com/collabora/WhisperLive)
- [QuentinFuxa/WhisperLiveKit](https://github.com/QuentinFuxa/WhisperLiveKit)
- [ggml-org/whisper.cpp](https://github.com/ggml-org/whisper.cpp)
- [whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases)
- [whisper.cpp issue #1855 — variable audio_ctx](https://github.com/ggml-org/whisper.cpp/issues/1855)
- [whisper.cpp discussion #297 — audio_ctx semantics](https://github.com/ggml-org/whisper.cpp/discussions/297)
- [whisper.cpp issue #2722 — stream binary deprecation](https://github.com/ggml-org/whisper.cpp/issues/2722)
- [whisper.cpp issue #2867 — Vulkan AMD 7900 XT exits silently](https://github.com/ggml-org/whisper.cpp/issues/2867)
- [sandrohanea/whisper.net](https://github.com/sandrohanea/whisper.net)

### Composante B — VAD énergie

- [arXiv:2501.11378 — Whisper hallucinations on non-speech audio](https://arxiv.org/html/2501.11378v1)
- [arXiv:2505.12969 — Calm-Whisper](https://arxiv.org/html/2505.12969v1)
- [arXiv:2511.14219 — Listen Like a Teacher (ALA + distillation)](https://arxiv.org/pdf/2511.14219)
- [arXiv:2402.09797 — Multi-channel VAD benchmark](https://arxiv.org/pdf/2402.09797)
- [Voice Activity Detection With Adaptive Thresholding — VOCAL](https://vocal.com/voice-quality-enhancement/voice-activity-detection-with-adaptive-thresholding/)
- [Approach for Energy-Based Voice Detector — IAENG](https://www.iaeng.org/IJCS/issues_v36/issue_4/IJCS_36_4_16.pdf)
- [rymshasaeed/Voice-Activity-Detection (Python RMS+ZCR ref)](https://github.com/rymshasaeed/Voice-Activity-Detection)
- [TEN-framework/ten-vad](https://github.com/TEN-framework/ten-vad)
- [Picovoice VAD Benchmark](https://picovoice.ai/docs/benchmark/vad/)
- [Latency in Speech Recognition — Picovoice blog](https://picovoice.ai/blog/latency-in-speech-recognition/)
- [Silero VAD overview — Stackademic](https://blog.stackademic.com/silero-vad-the-lightweight-high-precision-voice-activity-detector-26889a862636)
- [ladenedge/WebRtcVadSharp](https://github.com/ladenedge/WebRtcVadSharp)
- [WebRtcVad.NET (managed C# port)](https://libraries.io/nuget/WebRtcVad.NET)
- [dpirch/libfvad](https://github.com/dpirch/libfvad)
- [whisper.cpp issue #1724 — hallucination on silence](https://github.com/ggml-org/whisper.cpp/issues/1724)
- [openai/whisper discussion #2378 — preprocessing](https://github.com/openai/whisper/discussions/2378)

### Composante C — distil-fr

- [arXiv:2311.00430 — Distil-Whisper](https://arxiv.org/abs/2311.00430)
- [arXiv:2407.03211 — Cohere quantization study](https://arxiv.org/abs/2407.03211)
- [bofenghuang/whisper-large-v3-french-distil-dec16](https://huggingface.co/bofenghuang/whisper-large-v3-french-distil-dec16)
- [bofenghuang/whisper-large-v3-french-distil-dec2](https://huggingface.co/bofenghuang/whisper-large-v3-french-distil-dec2)
- [bofenghuang/whisper-large-v3-distil-fr-v0.2](https://huggingface.co/bofenghuang/whisper-large-v3-distil-fr-v0.2)
- [eustlb/distil-large-v3-fr](https://huggingface.co/eustlb/distil-large-v3-fr)
- [eustlb/distil-large-v3-fr-ggml](https://huggingface.co/eustlb/distil-large-v3-fr-ggml)
- [distil-whisper/distil-large-v3-ggml discussion #1 — FP16 confirmation](https://huggingface.co/distil-whisper/distil-large-v3-ggml/discussions/1)
- [whisper.cpp issue #1423 — Distil-Whisper support](https://github.com/ggml-org/whisper.cpp/issues/1423)
- [whisper.cpp issue #1711 — convert HeaderTooLarge](https://github.com/ggml-org/whisper.cpp/issues/1711)
- [whisper.cpp models/README.md](https://github.com/ggml-org/whisper.cpp/blob/master/models/README.md)
- [huggingface/distil-whisper](https://github.com/huggingface/distil-whisper)

### Ancrages doctrine Deckle

- [ADR-0007 — rester sur whisper.cpp, surveiller Voxtral](../adr/0007-rester-sur-whisper-cpp-surveiller-voxtral.md)
- [ADR-0008 — rester sur Vulkan pour les backends GPU](../adr/0008-rester-sur-vulkan-pour-backends-gpu-natifs.md)
- [ADR-0010 — backend ASR pluggable via IAsrBackend](../adr/0010-backend-asr-pluggable-via-iasrbackend.md)
- [ADR-0011 — corpus normalisé comme dataset ML](../adr/0011-corpus-normalise-comme-dataset-ml.md)
- [research--whisper-alternatives-fine-windowing--2026-05-27.md](research--whisper-alternatives-fine-windowing--2026-05-27.md)
- [reference--native-runtime--1.0.md](../reference/reference--native-runtime--1.0.md)
- [src/Deckle.Transcription/CLAUDE.md](../../src/Deckle.Transcription/CLAUDE.md)
- [src/Deckle.Transcription.Whisper/CLAUDE.md](../../src/Deckle.Transcription.Whisper/CLAUDE.md)
- [src/Deckle.Audio/CLAUDE.md](../../src/Deckle.Audio/CLAUDE.md)
- [benchmark/CLAUDE.md](../../benchmark/CLAUDE.md)
