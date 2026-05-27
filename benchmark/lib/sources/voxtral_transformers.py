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

Deux modes d'invocation, choisis par heuristique sur le prompt fourni :

  - **Canonique** (``processor.apply_transcription_request``) : quand
    ``prompt`` et ``system_prompt`` sont tous deux vides. La méthode
    officielle Mistral accepte ``language`` + ``audio`` + ``model_id``
    et injecte un template fixe avec le token spécial Voxtral
    ``[TRANSCRIBE]`` implicite. Sortie : transcription pure, lissée par
    défaut. C'est précisément ce que ``llama-mtmd-cli`` ne fait pas.
  - **Chat** (``processor.apply_chat_template``) : quand un ``prompt``
    ou ``system_prompt`` est fourni. Format multimodal officiel Mistral
    avec un ``role: user`` mixant un bloc ``{"type": "audio"}`` et un
    bloc ``{"type": "text"}``. Voxtral suit l'instruction (verbatim,
    traduction, résumé, classification de ton, etc.).

Voxtral ne supporte pas le rôle ``system`` séparément (cf. model card
Mistral) — quand ``system_prompt`` est fourni, son contenu est
concaténé devant le ``prompt`` utilisateur dans un seul bloc text avec
une séparation par double saut de ligne.

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
        audio_s = _wav_duration_seconds(audio_path)

        if max_new_tokens is None:
            max_new_tokens = max(
                self._max_new_tokens_floor,
                int(math.ceil(audio_s * self._max_new_tokens_per_audio_s)),
            )

        torch = self._torch

        # Heuristique de routage : prompt et system_prompt tous deux
        # vides → mode canonique (apply_transcription_request, [TRANSCRIBE]
        # injecté implicitement). Sinon → mode chat (apply_chat_template).
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


def _build_chat_instruction(prompt: str, system_prompt: str | None) -> str:
    """Construit le bloc text unique du message user pour le mode chat.

    Voxtral ne supporte pas le rôle ``system`` séparément (cf. model card
    Mistral et ``voxtral_chat.py``). Quand ``system_prompt`` est fourni,
    son contenu précède le ``prompt`` utilisateur, séparé par un double
    saut de ligne — c'est la convention la plus proche d'un vrai rôle
    system tout en restant dans un message ``role: user``."""
    sp = (system_prompt or "").strip()
    up = (prompt or "").strip()
    if sp and up:
        return f"{sp}\n\n{up}"
    return sp or up
