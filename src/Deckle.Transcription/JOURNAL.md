---
description: Diagnosis notes and kept decisions for Deckle.Transcription — read on demand when chasing why something is the way it is, not on every visit.
type: module-journal
---

# JOURNAL — Deckle.Transcription

Not read by default. Come here when you need the *why* behind a choice that the code no longer shows.

## 2026-07-16 — Operational logs are content-free; telemetry joins by transcription id

Spoken text, prompt previews and segment text never enter operational events. Purpose-specific datasets remain the only content-bearing route and stay consent-gated. `transcription_id` joins the content-free operational correlation event with latency and corpus rows. Whole-output DPAPI encryption was not chosen: it would replace the inspectable JSONL/WAV contracts rather than repair the boundary between logs and datasets.

## 2026-06-13 — Hallucination filter is output-side; paragraph append unimplemented; ASR spike closed

- The "Sous-titrage Radio-Canada" near-silence hallucination is to be handled by an **output denylist** that strips known hallucination phrases before the text reaches the clipboard/paste — not by the adaptive-segmenter-threshold root fix once planned. The phrase may still be transcribed; it must never leave the pipeline into the clipboard.
- Paragraph mode starts a new paragraph on **every** silence cut regardless of utterance duration (`Engine/TranscriptionEngine.StreamingPipeline.cs`). The "< 30 s → append to the previous paragraph" rule is not implemented; its trigger criterion (utterance length? silence length?) is an open product choice.
- ASR backend alternatives (Voxtral / Phi-4) and the home-grown large-window streaming spike are set aside: whisper.cpp is sufficient at the current latency, and streaming is the working main mode.

## 2026-06-05 — Why the model prime is synchronous on the worker thread

Superseded on 2026-06-15 by `bacbda82`: capture and prime now start concurrently, with the first backend call joining the prime through a shared gate. The original boot-time detached warmup described below remains retired.

The earlier model warmup ran at boot on its own detached thread. It raced a real hotkey transcription: when the user dictated while the warmup inference was still running, priming text occasionally leaked to the clipboard. A `t_isWarmup` `ThreadStatic` flag was used to gate the warmup's user-facing tail (clipboard, rewrite, paste, status events) so it wouldn't surface — fragile, and it didn't close the race.

The fix moved priming onto the worker thread, synchronously, ahead of recording (`EnsurePrimed` at the top of `WorkerRun`). Synchronous-on-worker removes the race structurally — there is no second thread to collide with. The prime now also bypasses the pipeline entirely: it calls `IAsrBackend.TranscribeAsync` directly with an empty segment sink, so there is no user-facing tail to suppress and nothing to gate. `t_isWarmup` was removed as unnecessary.

Not to be confused with the former HUD *composition* warm (`PrimeAndHide` in `Deckle.App` / `Deckle.Hud`) — a boot-time hidden window show, since removed. The model prime described here is the only warmup left on the first-hotkey path.

## 2026-06-07 — Les trois mécanismes « silence », et pourquoi init/whisper est dégénéré

Le VAD interne de whisper a été tué de bout en bout (modèle ggml retiré du wizard, parser de logs `whisper_vad` + event `VadParsed` supprimés de `WhisperBackend`, `wparams.vad = 0` conservé comme interrupteur). Restent trois mécanismes que le mot « silence » fait confondre — à garder distincts (destination : un `CLAUDE.md` du module, à écrire lors de la passe Settings, pas avant) :

- **VAD externe Silero** (`Deckle.Vad`, toggle « Voice activity detection ») — agit sur la forme d'onde *avant* Whisper, découpe le silence. Le seul VAD actif.
- **Confidence thresholds** (`entropy_thold` / `logprob_thold` / `no_speech_thold`) — agissent sur la *sortie du décodeur* : retry à température montante, ou drop d'un segment flaggé no-speech. Garde-fous anti-hallucination, vivants. Ce n'est pas du VAD malgré le « treats it as silence ».
- **VAD interne de whisper** — ex-`wparams.vad` + Silero ggml intégré. Mort, plus aucun réglage exposé.

Conséquence télémétrie : `whisper_init_ms` / `whisper_ms` sont **dégénérés**. Le stopwatch d'init se fermait sur la première ligne de log du VAD interne (le signal « init terminée »). Sans ce VAD, il court jusqu'à la fin de `whisper_full` → `init ≈ total`, `whisper ≈ 0`, en monolithique comme en streaming (la roadmap ne le notait qu'en streaming). Les rendre justes demande un autre marqueur « init terminée » (une ligne de log whisper.cpp). Au passage, les colonnes mortes `vad_ms` / `vad_inference_ms` (et toute la chaîne `VadDurationMs` → `PipelineProduction.VadMs`) ont été retirées du schéma `latency.jsonl`.

Note logging : au split d'un provider EventSource, les consommateurs de noms de provider **en dur** doivent suivre. Ici le gate anti-firehose (`App.ShouldDropCaptureVerbose`) ne filtrait que `Deckle-Whisp` ; les events VAD Verbose ayant migré vers `Deckle-Vad`, il fallait les y ajouter, sinon le firehose par-utterance fuyait dans le log toggle OFF.
