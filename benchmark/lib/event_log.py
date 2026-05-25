"""Event log JSONL minimaliste pour observabilité bench.

Un event = une ligne JSON avec ``ts`` (ISO-8601), ``kind`` (string court),
et des champs libres selon le type. Format inspiré de Deckle.Diagnostics
mais simplifié (pas d'EventSource ici, juste du JSONL plat).

Pourquoi un event log :
  - ``results.jsonl`` capture le résultat de chaque row — pas le déroulé
    des étapes internes (start, fail, retry, cleanup, etc.).
  - Le monitor GPU PowerShell écrit son propre JSONL côté ressources.
    Joindre les deux sur ``ts`` permet de répondre à des questions du
    type « la VRAM a-t-elle saturé pendant la gen du sample 21 ? ».

Convention de kind :
  - ``bench_start`` / ``bench_end`` : début / fin du bench complet.
  - ``row_start`` / ``row_end`` : début / fin d'une row (sample × régime).
  - ``row_oom`` : OOM capté, suivi d'un ``row_retry`` ou ``row_fail``.
  - ``row_retry`` : tentative après cleanup.
  - ``model_load`` : chargement modèle (rare, 1× par run).
  - ``cleanup_gpu`` : gc + cache cleanup explicite.

Tous les events sont écrits par flush immédiat — si le process crashe,
l'historique reste lisible.
"""

from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path
from typing import Any, TextIO


class EventLog:
    """Writer JSONL append-only. Utilisé comme :

        log = EventLog(run_dir / "events.jsonl")
        log.event("row_start", sample_id="abc...", regime="V1")
        ...
        log.close()
    """

    def __init__(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        self._fp: TextIO = path.open("w", encoding="utf-8")
        self.path = path

    def event(self, kind: str, fields: dict | None = None, /, **kwargs: Any) -> None:
        """Accept both styles : ``event("x", {"k": v})`` ET
        ``event("x", k=v)`` — pratique parce que certaines callbacks
        construisent leur dict (cleanup_gpu) tandis que d'autres préfèrent
        kwargs (le bench).
        """
        entry: dict[str, Any] = {
            "ts":   datetime.now().isoformat(timespec="milliseconds"),
            "kind": kind,
        }
        if fields:
            entry.update(fields)
        if kwargs:
            entry.update(kwargs)
        self._fp.write(json.dumps(entry, ensure_ascii=False, default=str) + "\n")
        self._fp.flush()

    def close(self) -> None:
        try:
            self._fp.close()
        except Exception:
            pass

    def __enter__(self) -> "EventLog":
        return self

    def __exit__(self, *exc) -> None:
        self.close()
