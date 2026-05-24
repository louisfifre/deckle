"""Source Voxtral en mode transcription canonique Mistral.

Utilise ``processor.apply_transcription_request(audio, model_id, language)``,
la méthode officielle Mistral qui produit une transcription pure.

**Le ``prompt`` passé à ``transcribe()`` est ignoré par design.** Cette
méthode ne lit que ``audio`` + ``language`` + ``model_id``. C'est confirmé
par le code source ``mistral-common.TranscriptionRequest`` qui n'expose
pas de champ ``prompt`` ou ``instructions``. Pour donner des instructions
à Voxtral (verbatim, traduction, etc.), utiliser ``VoxtralChatSource``
qui passe par ``apply_chat_template``.

Comportement par défaut Voxtral en mode transcription :
  - Transcription lissée (ponctuation, accents corrects).
  - Pas d'hésitations conservées (pas de « euh », « hum »).
  - Pas de paraphrase ni reformulation (≠ chat mode où le modèle peut
    paraphraser).
  - Sur audio bruit / silence, sortie courte ou vide — plus prudent que
    Whisper qui hallucine "Sous-titrage Société Radio-Canada".

Validé Phase 3 (2026-05-24) : RTF 0.22 sur AMD RX 7900 XT, WER moyen
0.10 vs Whisper large-v3 référence (hors sample 1 où Whisper hallucine).
"""

from __future__ import annotations

import gc
import time
from pathlib import Path
from typing import Any, Callable

from ._base import Source, Transcription
from ._voxtral_common import (
    VoxtralBackend, adaptive_max_new_tokens, cleanup_gpu, is_oom_error,
    load_voxtral, wav_duration_seconds,
)


# Délai d'attente après un OOM avant de tenter à nouveau. Empirique :
# DirectML met du temps à libérer effectivement la VRAM allouée même
# après gc.collect, on lui laisse une fenêtre.
OOM_RETRY_SLEEP_S = 10.0


class VoxtralTranscribeSource(Source):
    """Voxtral en mode transcription pure (apply_transcription_request)."""

    name = "voxtral-transcribe"
    label = "Voxtral Mini 3B — transcription canonique (DirectML)"

    def __init__(
        self,
        *,
        backend: VoxtralBackend | None = None,
        dtype: str = "float16",
        device_index: int = 0,
        cpu: bool = False,
        model_id: str = "mistralai/Voxtral-Mini-3B-2507",
        language: str = "fr",
    ) -> None:
        """Si ``backend`` est passé, on le réutilise (utile si tu veux
        instancier les deux sources Voxtral côte à côte sans recharger
        le modèle). Sinon on charge le modèle ici."""
        self._backend = backend or load_voxtral(
            dtype=dtype, device_index=device_index, cpu=cpu, model_id=model_id,
        )
        self.language = language

        # Résolution du nom de méthode (la doc HF officielle de Voxtral
        # contient une typo `apply_transcrition_request` ; selon la version
        # de transformers, c'est l'un ou l'autre).
        proc = self._backend.processor
        self._apply_fn = (
            getattr(proc, "apply_transcription_request", None)
            or getattr(proc, "apply_transcrition_request", None)
        )
        if self._apply_fn is None:
            raise RuntimeError(
                "Le processor Voxtral n'expose ni apply_transcription_request "
                "ni apply_transcrition_request. Update transformers >=4.55."
            )

    @property
    def backend(self) -> VoxtralBackend:
        """Expose le backend pour qu'une autre source Voxtral puisse le
        réutiliser sans recharger le modèle."""
        return self._backend

    # ── Interface Source ───────────────────────────────────────────────

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,         # IGNORÉ : voir docstring module
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
    ) -> Transcription:
        audio_s = wav_duration_seconds(audio_path)
        if max_new_tokens is None:
            max_new_tokens = adaptive_max_new_tokens(audio_s)
        return self._run_with_retry(
            audio_path=audio_path,
            audio_s=audio_s,
            max_new_tokens=max_new_tokens,
            on_event=on_event,
        )

    def _run_with_retry(
        self,
        *,
        audio_path:     Path,
        audio_s:        float,
        max_new_tokens: int,
        on_event:       Callable[[str, dict], None] | None,
    ) -> Transcription:
        """Une tentative ; si OOM, cleanup_gpu(sleep=10s) + retry une fois.
        Le retry réduit max_new_tokens de 25% pour donner plus de marge."""
        attempts = 0
        current_tokens = max_new_tokens
        last_err = ""
        while attempts < 2:
            try:
                return self._run_once(
                    audio_path=audio_path,
                    audio_s=audio_s,
                    max_new_tokens=current_tokens,
                    attempt=attempts,
                )
            except Exception as e:
                last_err = f"{type(e).__name__}: {e}"
                if not is_oom_error(e):
                    # Erreur non-OOM : on n'insiste pas.
                    if on_event:
                        on_event("row_fail", {"error": last_err, "audio_s": audio_s})
                    return _fail(last_err, audio_s, self._backend)
                # OOM : cleanup + retry
                if on_event:
                    on_event("row_oom_caught", {
                        "error": last_err, "audio_s": audio_s,
                        "max_new_tokens": current_tokens, "attempt": attempts,
                    })
                cleanup_gpu(sleep_s=OOM_RETRY_SLEEP_S, on_event=on_event)
                attempts += 1
                # Retry agressif : divise par 2 (les retries 0.75 ne
                # suffisaient pas sur les audios ~128s).
                current_tokens = max(256, int(current_tokens * 0.5))
                if on_event and attempts < 2:
                    on_event("row_retry_start", {
                        "audio_s": audio_s, "new_max_tokens": current_tokens,
                    })
        # Deux tentatives échouées
        if on_event:
            on_event("row_fail", {"error": last_err, "audio_s": audio_s, "attempts": attempts})
        return _fail(f"OOM after retry: {last_err}", audio_s, self._backend)

    def _run_once(
        self,
        *,
        audio_path:     Path,
        audio_s:        float,
        max_new_tokens: int,
        attempt:        int,
    ) -> Transcription:
        b = self._backend
        t0 = time.perf_counter()
        t_prep = time.perf_counter()
        inputs = self._apply_fn(
            language=self.language,
            audio=str(audio_path),
            model_id=b.model_id,
        )
        inputs = inputs.to(b.device, dtype=b.dtype)
        dt_prep = time.perf_counter() - t_prep

        t_gen = time.perf_counter()
        with b.torch.no_grad():
            generated_ids = b.model.generate(
                **inputs,
                max_new_tokens=max_new_tokens,
                do_sample=False,
            )
        dt_gen = time.perf_counter() - t_gen

        input_len = inputs["input_ids"].shape[-1]
        new_tokens_t = generated_ids[:, input_len:]
        n_new = int(new_tokens_t.shape[-1])
        text = b.processor.batch_decode(
            new_tokens_t, skip_special_tokens=True,
        )[0].strip()

        elapsed = time.perf_counter() - t0
        del inputs, generated_ids, new_tokens_t
        gc.collect()

        return Transcription(
            text=text,
            elapsed_s=elapsed,
            audio_s=audio_s,
            rtf=elapsed / audio_s if audio_s > 0 else float("inf"),
            generated_tokens=n_new,
            ok=True,
            extras={
                "mode":          "transcription",
                "prep_s":        dt_prep,
                "gen_s":         dt_gen,
                "tok_per_s":     n_new / dt_gen if dt_gen > 0 else 0.0,
                "device":        b.device_label,
                "dtype":         b.dtype_label,
                "model_id":      b.model_id,
                "n_params":      b.n_params,
                "max_new_tokens": max_new_tokens,
                "input_len":     int(input_len),
                "prompt_used":   "",
                "attempt":       attempt,
            },
        )


def _fail(error: str, audio_s: float, backend: VoxtralBackend) -> Transcription:
    return Transcription(
        text="", elapsed_s=0.0, audio_s=audio_s, rtf=0.0,
        generated_tokens=-1, ok=False, error=error,
        extras={"mode": "transcription",
                "device": backend.device_label, "dtype": backend.dtype_label},
    )
