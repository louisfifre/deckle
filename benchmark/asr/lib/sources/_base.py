"""Contract every transcription source follows.

The constructor pays the model load; ``transcribe()`` must run many times
without reloading, since a bench calls it once per sample and regime.
Sources inherit ``Source`` or just duck-type it. The signature stays fixed
so a bench swaps one backend for another without change.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable


@dataclass(frozen=True)
class Transcription:
    """``rtf = elapsed_s / audio_s``; below 1.0 is faster than real time."""
    text:            str
    elapsed_s:       float
    audio_s:         float
    rtf:             float
    generated_tokens: int          # -1 when the source does not report it
    ok:              bool
    error:           str = ""
    # Source-specific values (prep_s, dtype, n_params); must stay JSON-serializable.
    extras:          dict[str, Any] = field(default_factory=dict)


class Source:
    name: str          # short id, such as "whisper-cpp"
    label: str         # human description for reports

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
    ) -> Transcription:
        """``max_new_tokens=None`` lets the source decide, usually from the
        audio duration so long clips are not truncated. ``on_event`` reports
        abnormal events (``row_oom_caught``, ``row_retry_start``); the bench
        plugs its ``EventLog`` there.
        """
        raise NotImplementedError

    def warmup(self) -> None:
        """Optional: one throwaway transcription to warm GPU caches."""
        return None
