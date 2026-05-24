"""Voxtral inference wrapper for the POC bench.

Loads ``mistralai/Voxtral-Mini-3B-2507`` once, then runs transcription
with a different system prompt per call. The same model instance is
reused across the 5 V1..V5 configs to make the 5-config sweep cheap
(model load is the expensive step, ~10–30s on cold GPU).

Reference : ADR-0011 (POC évaluation Voxtral).
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

DEFAULT_MODEL_ID = "mistralai/Voxtral-Mini-3B-2507"


@dataclass(frozen=True)
class VoxtralResult:
    """One transcription run on one audio file with one config."""
    text:             str
    generated_tokens: int
    elapsed_seconds:  float
    rtf:              float            # generated time / audio duration
    audio_seconds:    float
    config_name:      str


class VoxtralEngine:
    """Holds the loaded model + processor and exposes ``transcribe``.

    The model is loaded lazily on the first ``transcribe`` call so that
    ``__init__`` can be called cheaply even when no transcription is
    actually requested (e.g. in tests or dry-runs).
    """

    def __init__(
        self,
        *,
        model_id:        str  = DEFAULT_MODEL_ID,
        device:          str | None = None,   # "cuda", "cpu", or None=autodetect
        dtype:           Any  = None,         # torch.dtype or None=autodetect
    ) -> None:
        self.model_id  = model_id
        self._device   = device
        self._dtype    = dtype
        self._model    = None
        self._processor = None
        self._load_seconds: float | None = None

    # ── Lazy loading ──────────────────────────────────────────────────
    def _ensure_loaded(self) -> None:
        if self._model is not None:
            return
        import torch
        from transformers import AutoProcessor, VoxtralForConditionalGeneration

        if self._device is None:
            self._device = "cuda" if torch.cuda.is_available() else "cpu"
        if self._dtype is None:
            self._dtype = torch.bfloat16 if self._device == "cuda" else torch.float32

        t0 = time.time()
        self._processor = AutoProcessor.from_pretrained(self.model_id)
        self._model = VoxtralForConditionalGeneration.from_pretrained(
            self.model_id,
            torch_dtype=self._dtype,
            device_map=self._device,
        )
        self._load_seconds = time.time() - t0

    @property
    def device(self) -> str:
        self._ensure_loaded()
        return self._device

    @property
    def load_seconds(self) -> float | None:
        return self._load_seconds

    # ── Inference ─────────────────────────────────────────────────────
    def transcribe(
        self,
        *,
        audio_path:      Path,
        config_name:     str,
        system_prompt:   str,
        language:        str   = "fr",
        max_new_tokens:  int   = 2000,
    ) -> VoxtralResult:
        """Run one transcription. ``system_prompt`` drives the régime.

        The Voxtral processor exposes ``apply_transcrition_request``
        (sic — typo upstream in transformers 5.x) which is the canonical
        path for the transcription instruction template. We feed our
        régime-specific system prompt by passing it as an additional
        message field — if that turns out to be ignored by the template,
        we fall back to the chat API at the bench level.
        """
        self._ensure_loaded()
        import torch

        audio_seconds = _wav_duration_seconds(audio_path)

        # The transformers Voxtral integration accepts a system prompt
        # via the ``conversation`` payload. We build that conversation
        # explicitly here to keep régime control granular.
        conversation = [
            {"role": "system",
             "content": [{"type": "text", "text": system_prompt}]},
            {"role": "user",
             "content": [
                 {"type": "audio", "path": str(audio_path)},
             ]},
        ]
        inputs = self._processor.apply_chat_template(
            conversation,
            add_generation_prompt=True,
            tokenize=True,
            return_dict=True,
            return_tensors="pt",
        )
        inputs = inputs.to(self._device, dtype=self._dtype)

        t0 = time.time()
        with torch.inference_mode():
            outputs = self._model.generate(
                **inputs,
                max_new_tokens=max_new_tokens,
                do_sample=False,            # deterministic for comparison
            )
        elapsed = time.time() - t0

        prompt_len = inputs.input_ids.shape[1]
        new_tokens = int(outputs.shape[1] - prompt_len)
        decoded = self._processor.batch_decode(
            outputs[:, prompt_len:],
            skip_special_tokens=True,
        )
        text = decoded[0].strip() if decoded else ""

        rtf = elapsed / audio_seconds if audio_seconds > 0 else float("inf")
        return VoxtralResult(
            text=text,
            generated_tokens=new_tokens,
            elapsed_seconds=elapsed,
            rtf=rtf,
            audio_seconds=audio_seconds,
            config_name=config_name,
        )


# ── Helper : durée WAV sans charger les samples en mémoire ───────────
def _wav_duration_seconds(path: Path) -> float:
    """Lecture du header WAV pour la durée. Si format non-WAV, retombe
    sur ``librosa`` qui couvre la quasi-totalité des formats audio mais
    coûte une lecture complète."""
    import wave
    try:
        with wave.open(str(path), "rb") as wf:
            frames = wf.getnframes()
            rate = wf.getframerate()
            return frames / float(rate) if rate else 0.0
    except (wave.Error, EOFError):
        import soundfile as sf
        info = sf.info(str(path))
        return float(info.duration)
