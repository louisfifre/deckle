---
name: research-energy-segmenter-params-2026-06-02
description: "Récap de recherche web (2026-06-02) sur les paramètres standards de segmentation parole/silence — défauts Silero VAD, endpointing ASR, VAD basé énergie. Matière brute de référence."
type: research
date: 2026-06-02
---

# Paramètres de segmentation VAD / endpointing — recherche (2026-06-02)

> Sources : trois recherches web ciblées en session 2026-06-02 (défauts Silero VAD ; endpointing ASR ; VAD basé énergie). Liens en bas. *Valeurs issues d'outils conçus pour d'autres usages (détection de parole, endpointing conversationnel temps réel) — matière de référence, ni transposée ni testée sur un cas Deckle.*

## Contexte de la recherche

Avant de dimensionner un découpage parole/silence maison, relever les paramètres standards employés par les outils et la littérature établis (seuils, durées de silence, marges), plutôt que d'inventer des valeurs.

## Findings

### Silero VAD — défauts publiés

`threshold = 0.5` · `min_speech_duration_ms = 250` · `min_silence_duration_ms = 100` · `speech_pad_ms = 30`. `min_speech_duration_ms` **jette** les segments de parole plus courts que le seuil (filtrage anti-bruit). `min_silence_duration_ms` est le silence attendu en fin de segment de parole avant de le séparer. `speech_pad_ms` ajoute du padding de chaque côté du segment retenu.

### Endpointing ASR — durée de silence de fin

Défaut Deepgram : **400 ms** de silence pour décider la fin d'un énoncé. Les VAD traditionnels attendent souvent jusqu'à ~700 ms de silence de traîne (plus sûr, plus de latence). Les approches sémantiques descendent à 300 ms quand une ponctuation finale est détectée, 400 ms sinon. Compromis structurant : endpointing agressif (faible latence, risque de couper un mot) vs conservateur (sûr, plus lent). Les moteurs établis (Kaldi `OnlineEndpointRule`) combinent la longueur du silence de traîne **et** une longueur minimale d'utterance.

### VAD basé énergie — seuil, hangover, layering

Le seuil compare l'énergie court-terme à une valeur idéalement calée sur les périodes de bruit seul (adaptatif). Décision par frames de 5-40 ms. **Hangover** : on retarde la transition parole→silence pour ne pas classer une fin de mot faible (consonne qui s'éteint) comme silence ; le **hangbefore** en est le symétrique en début de parole. Pratique de production rapportée : superposer un *energy gate* (latence milliseconde), un modèle statistique (stabilité) et un réseau neuronal (précision quand le bruit devient imprévisible).

## Sources

- Silero VAD — défauts : <https://github.com/snakers4/silero-vad/blob/master/src/silero_vad/utils_vad.py>
- Deepgram — VAD / endpointing : <https://deepgram.com/learn/voice-activity-detection>
- Endpointing sémantique 300-700 ms (arXiv 2401.08916) : <https://arxiv.org/html/2401.08916v1>
- Kaldi `OnlineEndpointRule` : <https://kaldi-asr.org/doc/structkaldi_1_1OnlineEndpointRule.html>
- VAD énergie + hangover (short-time energy) : <https://superkogito.github.io/blog/2020/02/09/naive_vad.html>
