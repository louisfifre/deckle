"""Voxtral Mini 3B in BF16 over Transformers and torch ROCm on Windows.

Safetensors in BF16, the precision a shipping speech model needs; a 4-bit
quantization serves smoke tests only. The model loads once in the
constructor and serves every ``transcribe()`` call.

Stack:
  - torch 2.9.1+rocm7.2.1 (AMD's official Windows wheel)
  - transformers >=4.56, <5.0 (pinned 4.57.6); 5.x brings back an import
    failure through ``continuous_batching``
  - mistral-common 1.11.2 [audio]
  - accelerate (``device_map`` needs it)
  - librosa (``load_audio_as`` needs it)

With no ``prompt`` and no ``system_prompt``, the source uses Mistral's
canonical ``apply_transcription_request``, which injects the
``[TRANSCRIBE]`` template that ``llama-mtmd-cli`` lacks. Otherwise it uses
``apply_chat_template`` and Voxtral follows the instruction.
"""

from __future__ import annotations

import math
import os
import time
from pathlib import Path
from typing import Callable

from ._base import Source, Transcription


# The local path loads the weights, but ``apply_transcription_request``
# still needs the Hugging Face id to resolve its template through
# mistral-common.
HF_REPO_ID = "mistralai/Voxtral-Mini-3B-2507"


class VoxtralTransformersSource(Source):
    name = "voxtral-transformers"
    label = "Voxtral Mini 3B BF16 via Transformers (ROCm)"

    def __init__(
        self,
        *,
        model_path: Path | str | None = None,
        dtype:      "object | None"  = None,    # torch.dtype; torch is imported lazily
        device:     str               = "cuda",
        hf_repo_id: str               = HF_REPO_ID,
        max_new_tokens_floor:        int   = 128,
        max_new_tokens_per_audio_s:  float = 8.0,
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

    # ── Source interface ───────────────────────────────────────────────

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
        system_prompt:  str | None = None,
    ) -> Transcription:
        audio_s = _wav_duration_seconds(audio_path)

        if max_new_tokens is None:
            max_new_tokens = max(
                self._max_new_tokens_floor,
                int(math.ceil(audio_s * self._max_new_tokens_per_audio_s)),
            )

        torch = self._torch

        use_chat = bool(prompt) or bool(system_prompt)
        chat_instruction = _build_chat_instruction(prompt, system_prompt) if use_chat else ""

        t0 = time.perf_counter()
        try:
            if use_chat:
                conversation = [{
                    "role": "user",
                    "content": [
                        {"type": "audio", "path": str(audio_path)},
                        {"type": "text",  "text": chat_instruction},
                    ],
                }]
                inputs = self._processor.apply_chat_template(conversation)
                mode_label = "apply_chat_template"
            else:
                inputs = self._processor.apply_transcription_request(
                    language="fr",
                    audio=str(audio_path),
                    model_id=self._hf_repo_id,
                )
                mode_label = "apply_transcription_request"

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
                "mode":          mode_label,
                "chat_instruction": chat_instruction if use_chat else "",
                "system_prompt_concatenated": bool(use_chat and system_prompt and prompt),
            },
        )

    def warmup(self) -> None:
        """No warmup: the first inference pays the HIP JIT, and the bench's
        own loop absorbs that on its first sample."""
        return None


# ── Helpers ───────────────────────────────────────────────────────────


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


def _build_chat_instruction(prompt: str, system_prompt: str | None) -> str:
    """Voxtral has no separate ``system`` role, so ``system_prompt`` goes
    before the user prompt in the one ``role: user`` text block."""
    sp = (system_prompt or "").strip()
    up = (prompt or "").strip()
    if sp and up:
        return f"{sp}\n\n{up}"
    return sp or up
