"""Join the bench's events with the system samples ``gpu_monitor.ps1``
records beside it, by time window (both logs use ISO 8601 milliseconds).

Per-row peaks land as a ``system`` key on each ``results.jsonl`` row; phase
peaks (idle baseline, model load, whole run) land as a ``bench_summary``
event at the end of ``events.jsonl``. Call it once the bench is over and
the monitor stopped: it reads two closed files, nothing incremental.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable


# gpu_monitor.ps1's nominal cadence, bounded by Get-Counter's 1 s minimum
# sample interval. Only feeds the theoretical ``coverage_ratio``; load on
# the machine can stretch the real cadence above it.
_MONITOR_SAMPLE_INTERVAL_S = 1.0


@dataclass(frozen=True)
class MonitorSample:
    """Units: MB for memory, percent for utilization, MB/s for disk
    throughput, and the raw instantaneous queue length for
    ``disk_queue_length``.
    """
    ts:                datetime
    ram_used_mb:       float
    ram_pct:           float
    cpu_pct:           float
    disk_read_mb_s:    float
    disk_write_mb_s:   float
    disk_queue_length: float
    vram_dedicated_mb: float
    vram_shared_mb:    float
    gpu_compute_pct:   float


@dataclass(frozen=True)
class PeakStats:
    peak_vram_dedicated_mb: float
    peak_vram_shared_mb:    float
    peak_gpu_compute_pct:   float
    peak_ram_used_mb:       float
    peak_ram_pct:           float
    peak_cpu_pct:           float
    peak_disk_read_mb_s:    float
    peak_disk_write_mb_s:   float
    peak_disk_queue_length: float
    samples_count:          int
    coverage_ratio:         float = 1.0   # fraction of the window covered by samples


# ── Parsing ────────────────────────────────────────────────────────────

def _parse_ts(raw: str) -> datetime:
    return datetime.fromisoformat(raw)


def load_monitor_samples(path: Path) -> list[MonitorSample]:
    """Skip blank or malformed lines: the monitor can be stopped mid-write."""
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
                # Older monitor logs lack the cpu and disk keys; they read as 0.
                samples.append(MonitorSample(
                    ts=_parse_ts(obj["ts"]),
                    ram_used_mb=float(obj.get("ram_used_mb", 0.0) or 0.0),
                    ram_pct=float(obj.get("ram_pct", 0.0) or 0.0),
                    cpu_pct=float(obj.get("cpu_pct", 0.0) or 0.0),
                    disk_read_mb_s=float(obj.get("disk_read_mb_s", 0.0) or 0.0),
                    disk_write_mb_s=float(obj.get("disk_write_mb_s", 0.0) or 0.0),
                    disk_queue_length=float(obj.get("disk_queue_length", 0.0) or 0.0),
                    vram_dedicated_mb=float(obj.get("vram_dedicated_mb", 0.0) or 0.0),
                    vram_shared_mb=float(obj.get("vram_shared_mb", 0.0) or 0.0),
                    gpu_compute_pct=float(obj.get("gpu_compute_pct", 0.0) or 0.0),
                ))
            except (KeyError, ValueError, TypeError):
                continue
    samples.sort(key=lambda s: s.ts)
    return samples


def load_events(path: Path) -> list[dict[str, Any]]:
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
    if end_ts < start_ts:
        return None
    in_window = [s for s in samples if start_ts <= s.ts <= end_ts]
    if not in_window:
        return None

    duration = (end_ts - start_ts).total_seconds()
    expected = max(1.0, duration / _MONITOR_SAMPLE_INTERVAL_S)
    coverage = min(1.0, len(in_window) / expected)

    return PeakStats(
        peak_vram_dedicated_mb=max(s.vram_dedicated_mb for s in in_window),
        peak_vram_shared_mb=max(s.vram_shared_mb for s in in_window),
        peak_gpu_compute_pct=max(s.gpu_compute_pct for s in in_window),
        peak_ram_used_mb=max(s.ram_used_mb for s in in_window),
        peak_ram_pct=max(s.ram_pct for s in in_window),
        peak_cpu_pct=max(s.cpu_pct for s in in_window),
        peak_disk_read_mb_s=max(s.disk_read_mb_s for s in in_window),
        peak_disk_write_mb_s=max(s.disk_write_mb_s for s in in_window),
        peak_disk_queue_length=max(s.disk_queue_length for s in in_window),
        samples_count=len(in_window),
        coverage_ratio=round(coverage, 3),
    )


def _peak_dict(p: PeakStats | None) -> dict[str, Any] | None:
    if p is None:
        return None
    return {
        "peak_vram_dedicated_mb": p.peak_vram_dedicated_mb,
        "peak_vram_shared_mb":    p.peak_vram_shared_mb,
        "peak_gpu_compute_pct":   p.peak_gpu_compute_pct,
        "peak_ram_used_mb":       p.peak_ram_used_mb,
        "peak_ram_pct":           p.peak_ram_pct,
        "peak_cpu_pct":           p.peak_cpu_pct,
        "peak_disk_read_mb_s":    p.peak_disk_read_mb_s,
        "peak_disk_write_mb_s":   p.peak_disk_write_mb_s,
        "peak_disk_queue_length": p.peak_disk_queue_length,
        "samples_count":          p.samples_count,
        "coverage_ratio":         p.coverage_ratio,
    }


# ── Join ──────────────────────────────────────────────────────────────

def join_row_peaks(
    events:  list[dict[str, Any]],
    samples: list[MonitorSample],
) -> dict[tuple[str, str], dict[str, Any]]:
    """When a (sample_id, regime) pair runs twice (replay, retry), the last
    row wins, so the peaks match the result that was kept."""
    out: dict[tuple[str, str], dict[str, Any]] = {}
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
    """A phase is None when the run lacks its events."""
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
        # The bench pauses between bench_start and model_load_start so the
        # monitor records an idle baseline before the model fills VRAM.
        # Without model_load_start the window is empty.
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
    """Without a monitor log (``--skip-monitor``) nothing is written. Each
    call appends one more ``bench_summary`` event to ``events.jsonl``.
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
            f"({p['samples_count']} samples, cov {p.get('coverage_ratio', 0):.2f})"
        )
        # Older runs lack the cpu and disk peaks.
        lines.append(
            f"  {'':<14}   "
            f"CPU {p.get('peak_cpu_pct', 0.0):>5.1f}%, "
            f"disk R/W {p.get('peak_disk_read_mb_s', 0.0):>5.1f}/"
            f"{p.get('peak_disk_write_mb_s', 0.0):<5.1f} MB/s, "
            f"queue {p.get('peak_disk_queue_length', 0.0):>4.1f}"
        )
    lines.append(f"  monitor samples lus : {summary.get('samples_count', 0)}")
    return "\n".join(lines)
