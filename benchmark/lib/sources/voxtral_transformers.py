"""Source Voxtral Mini 3B BF16 via Transformers + torch ROCm Windows.

Voie d'inférence safetensors-native ouverte par le pivot du
2026-05-27 — voir entrée correspondante dans ``benchmark/JOURNAL.md``.
Charge le modèle une seule fois dans le constructeur (coût ~8s, VRAM
~8.7 GiB sur RX 7900 XT en BF16), puis réutilise pour chaque appel à
``transcribe()``.

Stack actuelle (épinglée par le sanity check 2026-05-27) :
  - torch 2.9.1+rocm7.2.1 (wheel officiel AMD pour Windows)
  - transformers 4.57.6 (fenêtre stable >=4.56, <5.0 ; la 5.x ré-introduit
    le bug d'import via ``continuous_batching``)
  - mistral-common 1.11.2 [audio]
  - accelerate (requis par ``device_map``)
  - librosa (requis par ``load_audio_as``)

Mode supporté à ce stade : **transcription pure** (mode T1 baseline).
La méthode officielle ``processor.apply_transcription_request`` accepte
``language`` + ``audio`` + ``model_id`` mais **n'accepte pas de prompt
utilisateur** : elle injecte un template fixe avec le token spécial
Voxtral ``[TRANSCRIBE]`` implicite. C'est précisément ce que
llama-mtmd-cli ne fait pas correctement.

Pour les régimes T2-T6 (instructions utilisateur + system prompt), il
faut passer en mode chat via ``apply_chat_template`` — non implémenté
ici, voir le ``NotImplementedError`` ciblé. Premier run de validation
mesuré sur T1_baseline seul.

Référence : ``mistralai/Voxtral-Mini-3B-2507`` model card et
``docs/transformers/model_doc/voxtral``.
"""

from __future__ import annotations

import math
import os
import time
from pathlib import Path
from typing import Callable

from ._base import Source, Transcription


# Identifiant HF officiel pour résoudre les templates côté processor.
# Le path local sert au chargement des poids ; le model_id sert à
# ``apply_transcription_request`` qui résout le bon template via
# mistral-common. Les deux sont nécessaires.
HF_REPO_ID = "mistralai/Voxtral-Mini-3B-2507"


class VoxtralTransformersSource(Source):
    """Voxtral Mini 3B BF16 via Transformers (ROCm Windows)."""

    name = "voxtral-transformers"
    label = "Voxtral Mini 3B BF16 via Transformers (ROCm)"

    def __init__(
        self,
        *,
        model_path: Path | str | None = None,
        dtype:      "object | None"  = None,    # torch.dtype, importé lazy
        device:     str               = "cuda",
        hf_repo_id: str               = HF_REPO_ID,
        max_new_tokens_floor:        int   = 128,
        max_new_tokens_per_audio_s:  float = 4.0,
    ) -> None:
        import torch
        from transformers import AutoProcessor, VoxtralForConditionalGeneration

        if dtype is None:
            dtype = torch.bfloat16

        self._model_path = Path(model_path or os.environ.get(
            "DECKLE_VOXTRAL_SAFETENSORS",
            r"D:\models\llm\voxtral\Voxtral-Mini-3B-2507-safetensors"))

        if not self._model_path.exists():
            raise FileNotFoundError(
                f"voxtral-transformers safetensors introuvables : {self._model_path}\n"
                f"  Définir DECKLE_VOXTRAL_SAFETENSORS ou passer model_path."
            )

        self._device = device
        self._dtype  = dtype
        self._hf_repo_id = hf_repo_id
        self._max_new_tokens_floor       = max_new_tokens_floor
        self._max_new_tokens_per_audio_s = max_new_tokens_per_audio_s

        self._torch = torch
        self._processor = AutoProcessor.from_pretrained(self._model_path)
        self._model = VoxtralForConditionalGeneration.from_pretrained(
            self._model_path,
            dtype=dtype,
            device_map=device,
        )

    # ── Interface Source ───────────────────────────────────────────────

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
        system_prompt:  str | None = None,
    ) -> Transcription:
        # Cette source ne supporte que le mode transcription pur — qui
        # ignore prompt utilisateur et system_prompt. Si l'appelant fournit
        # un system_prompt non-vide, c'est qu'il vise un régime chat (T6)
        # qui n'est pas implémenté ici : on retourne ok=False clairement.
        if system_prompt:
            return _unsupported(
                audio_path,
                "regime chat (system_prompt) non implémenté par voxtral-transformers ; "
                "utiliser voxtral-llamacpp pour les régimes T2-T6.",
            )

        audio_s = _wav_duration_seconds(audio_path)

        if max_new_tokens is None:
            max_new_tokens = max(
                self._max_new_tokens_floor,
                int(math.ceil(audio_s * self._max_new_tokens_per_audio_s)),
            )

        torch = self._torch

        t0 = time.perf_counter()
        try:
            inputs = self._processor.apply_transcription_request(
                language="fr",
                audio=str(audio_path),
                model_id=self._hf_repo_id,
            )
            inputs = inputs.to(self._device, dtype=self._dtype)
            input_tokens = inputs.input_ids.shape[1]

            with torch.no_grad():
                outputs = self._model.generate(
                    **inputs,
                    max_new_tokens=max_new_tokens,
                    do_sample=False,
                )
            if torch.cuda.is_available():
                torch.cuda.synchronize()

            generated = outputs.shape[1] - input_tokens
            text = self._processor.batch_decode(
                outputs[:, input_tokens:],
                skip_special_tokens=True,
            )[0].strip()
        except Exception as e:
            elapsed = time.perf_counter() - t0
            return Transcription(
                text="", elapsed_s=elapsed, audio_s=audio_s,
                rtf=elapsed / audio_s if audio_s > 0 else 0.0,
                generated_tokens=-1, ok=False,
                error=f"{type(e).__name__}: {e}",
            )

        elapsed = time.perf_counter() - t0
        return Transcription(
            text=text,
            elapsed_s=elapsed,
            audio_s=audio_s,
            rtf=elapsed / audio_s if audio_s > 0 else float("inf"),
            generated_tokens=generated,
            ok=True,
            extras={
                "model_path":    str(self._model_path),
                "hf_repo_id":    self._hf_repo_id,
                "dtype":         str(self._dtype),
                "device":        self._device,
                "input_tokens":  input_tokens,
                "max_new_tokens": max_new_tokens,
                "mode":          "apply_transcription_request",
                "user_prompt_ignored":   bool(prompt),
            },
        )

    def warmup(self) -> None:
        """Charge déjà le modèle au __init__ ; pas de second warmup utile.

        Note : la première inférence post-load est plus lente (~10s vs
        ~2s en steady-state à cause du JIT HIP). Le bench fait sa propre
        boucle donc l'overhead est isolé sur le premier sample sans nous
        en occuper ici.
        """
        return None


# ── Helpers libres ────────────────────────────────────────────────────


def _wav_duration_seconds(path: Path) -> float:
    import wave
    try:
        with wave.open(str(path), "rb") as wf:
            frames = wf.getnframes()
            rate   = wf.getframerate()
            return frames / float(rate) if rate else 0.0
    except (wave.Error, EOFError):
        import soundfile as sf
        info = sf.info(str(path))
        return float(info.duration)


def _unsupported(audio_path: Path, reason: str) -> Transcription:
    audio_s = _wav_duration_seconds(audio_path)
    return Transcription(
        text="", elapsed_s=0.0, audio_s=audio_s, rtf=0.0,
        generated_tokens=-1, ok=False, error=reason,
    )
