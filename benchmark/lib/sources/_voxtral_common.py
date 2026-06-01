"""Factorisation du chargement Voxtral + helpers communs aux deux modes
(transcription canonique et chat). Inclut aussi la gestion VRAM
défensive (cleanup_gpu, retry-on-OOM) parce que DirectML accumule
des allocations entre rows et finit par OOM sur les longs.

Voxtral expose deux modes d'invocation distincts côté transformers
(cf. recherche doc Mistral 2026-05-24) :

  - **Transcription mode** : ``processor.apply_transcription_request(audio,
    model_id, language)`` produit le format prompt de transcription
    canonique Mistral. Aucun prompt utilisateur libre — la sortie est
    une transcription propre, fidèle, lissée par défaut.
  - **Chat mode (Audio QA)** : ``processor.apply_chat_template(messages)``
    avec un message multimodal mixant ``{"type": "audio"}`` + ``{"type":
    "text"}`` permet d'instruire Voxtral (« translate to English »,
    « transcribe verbatim with hesitations »). System prompts pas
    supportés à date — l'instruction passe en ``role: user``.

Les deux modes partagent le même modèle + processor + device. Pour ne
pas charger Voxtral deux fois en VRAM si on instancie successivement
les deux sources, ce module fournit le chargement factorisé.

Pas de classe Source ici — c'est un détail d'implémentation des deux
sources concrètes (``voxtral_transcribe.py``, ``voxtral_chat.py``).
"""

from __future__ import annotations

import gc
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from .._base_compat import _ensure_stdout_utf8


DEFAULT_MODEL_ID = "mistralai/Voxtral-Mini-3B-2507"


@dataclass(frozen=True)
class VoxtralBackend:
    """Bundle de tout ce qu'une source Voxtral a besoin pour invoquer le
    modèle. Construit par ``load_voxtral`` et passé en arg du constructor
    de chaque source concrète."""
    torch:        Any       # module torch (lazy-imported)
    device:       Any       # torch.device (DML ou CPU)
    device_label: str       # ex. "dml:0 (AMD Radeon RX 7900 XT)"
    dtype:        Any       # torch.float16 ou torch.float32
    dtype_label:  str
    processor:    Any       # transformers AutoProcessor
    model:        Any       # transformers VoxtralForConditionalGeneration
    model_id:     str
    n_params:     int


def load_voxtral(
    *,
    dtype: str = "float16",
    device_index: int = 0,
    cpu: bool = False,
    model_id: str = DEFAULT_MODEL_ID,
) -> VoxtralBackend:
    """Charge Voxtral sur le device cible. Coûteux (16-20 s cold start).

    Lazy import torch / torch_directml / transformers pour qu'un script
    qui n'instancie pas Voxtral (ex. bench Whisper-cpp seul) ne paie pas
    le coût d'import (~1.3 s pour torch + ~2 s pour transformers).
    """
    _ensure_stdout_utf8()

    import torch
    torch_dtype = torch.float16 if dtype == "float16" else torch.float32

    if cpu:
        device = torch.device("cpu")
        device_label = "cpu"
    else:
        import torch_directml as dml
        if not dml.is_available():
            raise RuntimeError(
                "torch_directml.is_available() = False. Passer cpu=True "
                "pour fallback, ou vérifier que le pilote AMD/Intel est à jour."
            )
        device = dml.device(device_index)
        # dml.device_name() peut renvoyer une chaîne avec un null byte
        # terminal (vu sur Radeon RX 7900 XT) ; on strip.
        dev_name = dml.device_name(device_index).rstrip("\x00").strip()
        device_label = f"dml:{device_index} ({dev_name})"

    from transformers import AutoProcessor, VoxtralForConditionalGeneration
    processor = AutoProcessor.from_pretrained(model_id)
    # Historiquement on passait ``torch_dtype=`` ; transformers récent émet
    # un DeprecationWarning et ignore le kwarg, ce qui chargeait le modèle
    # en fp32 (~19 GB en VRAM) au lieu du fp16 demandé (~9.4 GB). Le nom
    # canonique est ``dtype=``. L'assert ci-dessous vérifie après load que
    # la précision réelle est bien celle demandée — fail fast plutôt que
    # de silencieusement saturer la VRAM en aval.
    model = VoxtralForConditionalGeneration.from_pretrained(
        model_id,
        dtype=torch_dtype,
        low_cpu_mem_usage=True,
    )
    if model.dtype != torch_dtype:
        raise RuntimeError(
            f"Voxtral chargé en {model.dtype} alors que dtype={torch_dtype} "
            f"était demandé. transformers a peut-être ignoré le kwarg ; "
            f"vérifier la version de transformers et la signature de "
            f"VoxtralForConditionalGeneration.from_pretrained."
        )
    model = model.to(device)
    # Bascule en eval mode après transfert sur device. HuggingFace charge
    # par défaut en mode train(), qui garde des tensors intermediaires
    # pour le backward (BatchNorm running stats, dropout masks) — inutile
    # en inférence pure et coûteux en VRAM sur un 3B + audio encoder.
    # Un seul appel suffit, c'est idempotent.
    model.eval()

    return VoxtralBackend(
        torch=torch,
        device=device,
        device_label=device_label,
        dtype=torch_dtype,
        dtype_label=dtype,
        processor=processor,
        model=model,
        model_id=model_id,
        n_params=sum(p.numel() for p in model.parameters()),
    )


def cleanup_gpu(*, sleep_s: float = 0.0, on_event: Callable[[str, dict], None] | None = None) -> None:
    """Cleanup VRAM best-effort. DirectML n'expose pas d'API ``empty_cache``
    qui marche vraiment (``torch.xpu.synchronize`` plante "Torch not compiled
    with XPU enabled" sur notre stack). On s'en remet à :

      1. ``gc.collect()`` plusieurs passes pour casser les cycles de
         références qui retiennent des tenseurs GPU.
      2. ``torch.xpu.empty_cache()`` qui passe silencieusement sur DML
         mais ne fait pas de mal.
      3. Un ``sleep`` optionnel — empiriquement nécessaire après un OOM
         pour laisser DirectML libérer effectivement la mémoire allouée.

    Aucun retour de mesure VRAM ici (pas d'API DML pour ça). C'est le
    monitor PowerShell qui fournit la vue système.
    """
    for _ in range(3):
        gc.collect()
    try:
        import torch
        if hasattr(torch, "xpu") and hasattr(torch.xpu, "empty_cache"):
            torch.xpu.empty_cache()
    except Exception:
        pass
    if sleep_s > 0:
        if on_event:
            on_event("cleanup_gpu", {"sleep_s": sleep_s})
        time.sleep(sleep_s)


def is_oom_error(exc: BaseException) -> bool:
    """Heuristique : détecte une OOM côté DML / torch à partir du message.
    DML lève ``RuntimeError("Could not allocate tensor with X bytes")``,
    CUDA donnerait ``torch.cuda.OutOfMemoryError``. On reste large."""
    msg = str(exc).lower()
    return (
        "could not allocate" in msg
        or "out of memory" in msg
        or "outofmemory" in msg
        or "allocation failed" in msg
    )


def adaptive_max_new_tokens(audio_s: float, *, floor: int = 256, ceiling: int = 1024) -> int:
    """Estime un plafond de gen tokens à partir de la durée audio.

    Empirique : ~4 mots/seconde en FR × ~1.3 token/mot (BPE Mistral) ≈ 5
    tokens/s. Multiplicateur ×7 (marge raisonnable) borné [floor, ceiling].

    Le ceiling 1024 est calibré pour ne pas saturer la VRAM DirectML
    sur RX 7900 XT (20 GB) pour les audios ≥120 s — au-delà, l'OOM est
    fréquent même avec retry. Les très longs (>200 s) seront tronqués
    plutôt que de crasher — trade-off assumé.
    """
    estimate = int(audio_s * 7)
    return max(floor, min(ceiling, estimate))


def wav_duration_seconds(path: Path) -> float:
    """Lit la durée d'un WAV sans charger les données audio."""
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
