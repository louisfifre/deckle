"""Voxtral loading and helpers shared by the transcription and chat sources.

Both modes share one model, processor and device, so loading lives here and
both sources can reuse one backend instead of holding Voxtral twice in VRAM.
The defensive VRAM handling (cleanup_gpu, retry on OOM) exists because
DirectML accumulates allocations across rows until a long clip runs out of
memory.
"""

from __future__ import annotations

import gc
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from lib._base_compat import _ensure_stdout_utf8


DEFAULT_MODEL_ID = "mistralai/Voxtral-Mini-3B-2507"


@dataclass(frozen=True)
class VoxtralBackend:
    torch:        Any       # torch module, imported lazily
    device:       Any       # torch.device (DML or CPU)
    device_label: str       # e.g. "dml:0 (AMD Radeon RX 7900 XT)"
    dtype:        Any       # torch.float16 or torch.float32
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
    """torch, torch_directml and transformers are imported here, so a
    script that never loads Voxtral never pays for them."""
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
        # dml.device_name() can end with a NUL byte.
        dev_name = dml.device_name(device_index).rstrip("\x00").strip()
        device_label = f"dml:{device_index} ({dev_name})"

    from transformers import AutoProcessor, VoxtralForConditionalGeneration
    processor = AutoProcessor.from_pretrained(model_id)
    # ``dtype=``, not the deprecated ``torch_dtype=``: recent transformers
    # ignores the old kwarg and silently loads fp32, doubling VRAM. The
    # check below fails fast if the precision still differs.
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
    """Best effort: DirectML has no working ``empty_cache`` and no VRAM
    query. Several gc passes break the reference cycles that hold GPU
    tensors, ``torch.xpu.empty_cache()`` is a harmless no-op on DML, and
    after an OOM a sleep gives DirectML time to actually release memory.
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
    """Match on the message: DML raises a plain
    ``RuntimeError("Could not allocate tensor with X bytes")``, no
    dedicated type."""
    msg = str(exc).lower()
    return (
        "could not allocate" in msg
        or "out of memory" in msg
        or "outofmemory" in msg
        or "allocation failed" in msg
    )


def adaptive_max_new_tokens(audio_s: float, *, floor: int = 256, ceiling: int = 1024) -> int:
    """French speech yields about 5 tokens per second; 7 leaves margin.
    The ceiling bounds the KV cache, so a very long clip is truncated
    rather than running DirectML out of VRAM.
    """
    estimate = int(audio_s * 7)
    return max(floor, min(ceiling, estimate))


def wav_duration_seconds(path: Path) -> float:
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
