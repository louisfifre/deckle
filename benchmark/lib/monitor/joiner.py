"""Jointure events × monitor — relie l'activité logique du bench
(row_start, row_end, model_load_*, bench_start, bench_end) avec les
échantillons système (VRAM, GPU%, RAM) capturés en parallèle par le
script PowerShell ``gpu_monitor.ps1``.

Les deux JSONL partagent le même format de timestamp (ISO 8601
millisecondes) — la jointure se fait par intervalle temporel.

L'helper produit deux artefacts :

  - **Peaks par row** — pour chaque ``(sample_id, regime)`` qu'on a vu
    passer entre un ``row_start`` et un ``row_end``, on extrait la
    fenêtre temporelle et on calcule peak_vram_dedicated, peak_vram_
    shared, peak_gpu_compute, peak_ram. Enrichit ``results.jsonl`` en
    ajoutant une clé ``system`` à chaque row.

  - **Phase peaks** — peaks aux trois moments stratégiques du run :
    ``idle_baseline`` (avant ``bench_start``), ``model_load`` (entre
    ``model_load_start`` et ``model_load_end``, instrumenté côté bench),
    ``global_run`` (entre ``bench_start`` et ``bench_end``). Posés
    comme un nouvel event ``bench_summary`` à la fin de
    ``events.jsonl`` et imprimés en console.

Conçu pour être appelé en fin de bench une fois le monitor stoppé.
Pas de polling, pas de jointure incrémentale — on lit deux fichiers
fermés et on calcule.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable


@dataclass(frozen=True)
class MonitorSample:
    """Un échantillon système issu de gpu_monitor.ps1.

    Tous les champs en mégaoctets pour la VRAM/RAM et en pourcentage
    pour le GPU compute. ``ts`` est un datetime — parsé une fois au
    chargement pour éviter de reparser à chaque comparaison.
    """
    ts:                datetime
    ram_used_mb:       float
    ram_pct:           float
    vram_dedicated_mb: float
    vram_shared_mb:    float
    gpu_compute_pct:   float


@dataclass(frozen=True)
class PeakStats:
    """Peaks sur un intervalle temporel donné."""
    peak_vram_dedicated_mb: float
    peak_vram_shared_mb:    float
    peak_gpu_compute_pct:   float
    peak_ram_used_mb:       float
    peak_ram_pct:           float
    samples_count:          int
    coverage_ratio:         float = 1.0   # fraction d'intervalle couverte


# ── Parsing ────────────────────────────────────────────────────────────

def _parse_ts(raw: str) -> datetime:
    """Parse un timestamp ISO 8601 millisecondes. Tolère soit
    ``2026-05-24T12:34:56.789`` (le format émis par les deux sources),
    soit ``2026-05-24T12:34:56`` (au cas où une seconde-precise apparaît
    dans un futur log). ``fromisoformat`` mange les deux."""
    return datetime.fromisoformat(raw)


def load_monitor_samples(path: Path) -> list[MonitorSample]:
    """Charge tous les échantillons monitor dans l'ordre chronologique.
    Robuste à des lignes vides ou mal formées (un coup d'eau du
    process PS qui stoppe en pleine écriture)."""
    if not path.exists():
        return []
    samples: list[MonitorSample] = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            try:
                samples.append(MonitorSample(
                    ts=_parse_ts(obj["ts"]),
                    ram_used_mb=float(obj.get("ram_used_mb", 0.0) or 0.0),
                    ram_pct=float(obj.get("ram_pct", 0.0) or 0.0),
                    vram_dedicated_mb=float(obj.get("vram_dedicated_mb", 0.0) or 0.0),
                    vram_shared_mb=float(obj.get("vram_shared_mb", 0.0) or 0.0),
                    gpu_compute_pct=float(obj.get("gpu_compute_pct", 0.0) or 0.0),
                ))
            except (KeyError, ValueError, TypeError):
                continue
    samples.sort(key=lambda s: s.ts)
    return samples


def load_events(path: Path) -> list[dict[str, Any]]:
    """Charge tous les events bench dans l'ordre du fichier (déjà
    chronologique). Robuste aux mêmes accidents."""
    if not path.exists():
        return []
    events: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                continue
    return events


# ── Peaks ─────────────────────────────────────────────────────────────

def peaks_in_window(
    samples: list[MonitorSample],
    start_ts: datetime,
    end_ts:   datetime,
) -> PeakStats | None:
    """Extrait les peaks dans ``[start_ts, end_ts]``. Retourne None si
    aucun échantillon ne tombe dans l'intervalle (fenêtre trop courte
    ou monitor non démarré à ce moment-là)."""
    if end_ts < start_ts:
        return None
    in_window = [s for s in samples if start_ts <= s.ts <= end_ts]
    if not in_window:
        return None

    duration = (end_ts - start_ts).total_seconds()
    # Le monitor sample à 500 ms. Si la fenêtre dure < 500 ms et qu'on a
    # zéro échantillon, on est revenu None plus haut. Sinon coverage =
    # nombre d'échantillons × 0.5 / durée, capé à 1.0.
    expected = max(1.0, duration / 0.5)
    coverage = min(1.0, len(in_window) / expected)

    return PeakStats(
        peak_vram_dedicated_mb=max(s.vram_dedicated_mb for s in in_window),
        peak_vram_shared_mb=max(s.vram_shared_mb for s in in_window),
        peak_gpu_compute_pct=max(s.gpu_compute_pct for s in in_window),
        peak_ram_used_mb=max(s.ram_used_mb for s in in_window),
        peak_ram_pct=max(s.ram_pct for s in in_window),
        samples_count=len(in_window),
        coverage_ratio=round(coverage, 3),
    )


def _peak_dict(p: PeakStats | None) -> dict[str, Any] | None:
    """Sérialise un PeakStats en dict JSON-friendly."""
    if p is None:
        return None
    return {
        "peak_vram_dedicated_mb": p.peak_vram_dedicated_mb,
        "peak_vram_shared_mb":    p.peak_vram_shared_mb,
        "peak_gpu_compute_pct":   p.peak_gpu_compute_pct,
        "peak_ram_used_mb":       p.peak_ram_used_mb,
        "peak_ram_pct":           p.peak_ram_pct,
        "samples_count":          p.samples_count,
        "coverage_ratio":         p.coverage_ratio,
    }


# ── Jointure ──────────────────────────────────────────────────────────

def join_row_peaks(
    events:  list[dict[str, Any]],
    samples: list[MonitorSample],
) -> dict[tuple[str, str], dict[str, Any]]:
    """Apparie les ``row_start`` / ``row_end`` consécutifs pour la même
    paire (sample_id, regime), et calcule les peaks dans la fenêtre.

    Retourne ``{(sample_id, regime): {row_start_ts, row_end_ts, ...peaks}}``.
    Si plusieurs rows ont la même paire (rejeu, retry), la dernière
    écrase la précédente — on garde le résultat final.
    """
    out: dict[tuple[str, str], dict[str, Any]] = {}
    # On scanne les events dans l'ordre ; pour chaque row_start on
    # cherche le prochain row_end qui matche (sample_id, regime).
    open_rows: dict[tuple[str, str], datetime] = {}
    for ev in events:
        kind = ev.get("kind")
        sid  = ev.get("sample_id")
        reg  = ev.get("regime")
        if not (kind and sid and reg):
            continue
        key = (sid, reg)
        if kind == "row_start":
            open_rows[key] = _parse_ts(ev["ts"])
        elif kind == "row_end":
            start = open_rows.pop(key, None)
            if start is None:
                continue
            end = _parse_ts(ev["ts"])
            peaks = peaks_in_window(samples, start, end)
            entry: dict[str, Any] = {
                "row_start_ts": start.isoformat(timespec="milliseconds"),
                "row_end_ts":   end.isoformat(timespec="milliseconds"),
            }
            peak_d = _peak_dict(peaks)
            if peak_d is not None:
                entry.update(peak_d)
            out[key] = entry
    return out


def compute_phase_peaks(
    events:  list[dict[str, Any]],
    samples: list[MonitorSample],
) -> dict[str, dict[str, Any] | None]:
    """Calcule les peaks sur trois phases stratégiques :

      - ``idle_baseline`` : entre ``bench_start`` et ``model_load_start``.
        Cette fenêtre correspond à la pause que le bench insère
        volontairement entre les deux events précisément pour laisser
        au monitor le temps de capturer des samples avant que le
        chargement du modèle ne pousse la VRAM. Si ``model_load_start``
        est absent (vieux run sans instrumentation source), on tombe en
        rabattement sur ``bench_start`` comme borne haute — la fenêtre
        est alors vide.
      - ``model_load`` : entre ``model_load_start`` et ``model_load_end``,
        instrumenté par le bench autour de ``_build_source``.
      - ``global_run`` : entre ``bench_start`` et ``bench_end``.

    Chaque phase peut être ``None`` si l'event correspondant n'est pas
    présent (vieux run sans instrumentation model_load par exemple).
    """
    def find_ts(kind: str) -> datetime | None:
        for ev in events:
            if ev.get("kind") == kind:
                return _parse_ts(ev["ts"])
        return None

    bench_start  = find_ts("bench_start")
    bench_end    = find_ts("bench_end")
    load_start   = find_ts("model_load_start")
    load_end     = find_ts("model_load_end")

    phases: dict[str, dict[str, Any] | None] = {
        "idle_baseline": None,
        "model_load":    None,
        "global_run":    None,
    }

    if samples and bench_start is not None:
        # Idle = avant le début du chargement du modèle. Le bench émet
        # bench_start puis attend 10 s avant model_load_start, fenêtre
        # dans laquelle le monitor capte le baseline. Sans
        # model_load_start (rétrocompat) on retombe sur bench_start
        # comme borne haute — la fenêtre sera vide, ce que
        # `if idle_start < idle_end` gère.
        idle_end = load_start if load_start is not None else bench_start
        idle_start = max(samples[0].ts, bench_start)
        if idle_start < idle_end:
            phases["idle_baseline"] = _peak_dict(peaks_in_window(samples, idle_start, idle_end))

    if load_start is not None and load_end is not None:
        phases["model_load"] = _peak_dict(peaks_in_window(samples, load_start, load_end))

    if bench_start is not None and bench_end is not None:
        phases["global_run"] = _peak_dict(peaks_in_window(samples, bench_start, bench_end))

    return phases


# ── Orchestration ─────────────────────────────────────────────────────

def enrich_run(run_dir: Path) -> dict[str, Any]:
    """Lit ``run_dir/{events,monitor,results}.jsonl``, enrichit
    ``results.jsonl`` (chaque row gagne une clé ``system``), ajoute un
    event ``bench_summary`` à la fin de ``events.jsonl`` avec les phase
    peaks, et retourne le summary pour usage console.

    Idempotent : appelable plusieurs fois sur le même run. Si le monitor
    est absent (``--skip-monitor``), on n'enrichit rien et on retourne
    un summary vide. Aucune exception si un fichier est manquant.
    """
    events_path  = run_dir / "events.jsonl"
    monitor_path = run_dir / "monitor.jsonl"
    results_path = run_dir / "results.jsonl"

    samples = load_monitor_samples(monitor_path)
    events  = load_events(events_path)

    summary: dict[str, Any] = {
        "samples_count": len(samples),
        "phases":        compute_phase_peaks(events, samples),
    }

    if not samples:
        return summary

    # Enrichissement des rows.
    row_peaks = join_row_peaks(events, samples)
    if results_path.exists() and row_peaks:
        enriched_lines: list[str] = []
        with results_path.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    enriched_lines.append(line)
                    continue
                key = (row.get("audio_id"), row.get("regime"))
                if key in row_peaks:
                    row["system"] = row_peaks[key]
                enriched_lines.append(json.dumps(row, ensure_ascii=False))
        results_path.write_text("\n".join(enriched_lines) + "\n", encoding="utf-8")

    # Append du bench_summary dans events.jsonl.
    if events_path.exists():
        summary_event = {
            "ts":   datetime.now().isoformat(timespec="milliseconds"),
            "kind": "bench_summary",
            **summary,
        }
        with events_path.open("a", encoding="utf-8") as f:
            f.write(json.dumps(summary_event, ensure_ascii=False, default=str) + "\n")

    return summary


def format_summary_console(summary: dict[str, Any]) -> str:
    """Met en forme le summary pour affichage en fin de bench."""
    lines = ["=== system summary ==="]
    phases = summary.get("phases") or {}
    for name in ("idle_baseline", "model_load", "global_run"):
        p = phases.get(name)
        if p is None:
            lines.append(f"  {name:<14} : (non instrumenté ou pas d'échantillon)")
            continue
        lines.append(
            f"  {name:<14} : "
            f"VRAM ded {p['peak_vram_dedicated_mb']:>7.0f} MB, "
            f"GPU {p['peak_gpu_compute_pct']:>5.1f}%, "
            f"RAM {p['peak_ram_pct']:>4.1f}% "
            f"({p['samples_count']} samples)"
        )
    lines.append(f"  monitor samples lus : {summary.get('samples_count', 0)}")
    return "\n".join(lines)
