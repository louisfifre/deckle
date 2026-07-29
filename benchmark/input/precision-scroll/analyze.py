from __future__ import annotations

from pathlib import Path
from statistics import median
import argparse
import json
import math
import os
import time

from capture_analysis import (
    CaptureAnalysis,
    TrackpadGesture,
    WheelBurst,
    analyze_captures,
    histogram,
    quantiles,
)


def parse_args() -> argparse.Namespace:
    default_root = Path(
        os.environ.get("DECKLE_DATA_ROOT", Path(os.environ["LOCALAPPDATA"]) / "Deckle")
    )
    parser = argparse.ArgumentParser(
        description="Analyze Deckle trackpad and mouse-wheel telemetry without copying raw captures."
    )
    parser.add_argument("--data-root", type=Path, default=default_root)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--burst-gap-ms", type=float, default=250)
    parser.add_argument("--trackpad-limit", type=int, default=0)
    parser.add_argument("--wheel-limit", type=int, default=0)
    parser.add_argument(
        "--minimum-age-seconds",
        type=float,
        default=0,
        help="Optionally ignore captures modified more recently than this age.",
    )
    return parser.parse_args()


def select_files(
    directory: Path,
    pattern: str,
    limit: int,
    minimum_age_seconds: float,
) -> list[Path]:
    cutoff = time.time() - max(0, minimum_age_seconds)
    files = sorted(
        (path for path in directory.glob(pattern) if path.stat().st_mtime <= cutoff),
        key=lambda path: path.name,
        reverse=True,
    )
    return files[:limit] if limit > 0 else files


def normalized_metrics(analysis: CaptureAnalysis, gesture: TrackpadGesture) -> dict[str, float]:
    y_range = analysis.devices[gesture.device_key].y_range
    peak = gesture.peak_speed_y * 1000 / y_range
    terminal = gesture.terminal_speed_y * 1000 / y_range
    return {
        "duration_ms": gesture.duration_ms,
        "travel_surface": gesture.path_y / y_range,
        "displacement_surface": abs(gesture.displacement_y) / y_range,
        "peak_surface_s": peak,
        "median_surface_s": gesture.median_speed_y * 1000 / y_range,
        "terminal_surface_s": terminal,
        "terminal_ratio": terminal / max(peak, 1e-9),
    }


def summarize_gestures(analysis: CaptureAnalysis, synthetic: bool) -> dict[str, object]:
    gestures = [
        gesture
        for gesture in analysis.gestures
        if gesture.synthetic == synthetic and gesture.is_scroll_candidate
    ]
    metrics = [normalized_metrics(analysis, gesture) for gesture in gestures]

    def series(name: str) -> list[float]:
        return [float(item[name]) for item in metrics]

    def profile(name: str, selected: list[dict[str, float]]) -> dict[str, object]:
        def selected_series(metric: str) -> list[float]:
            return [float(item[metric]) for item in selected]

        return {
            "profile": name,
            "gestures": len(selected),
            "duration_ms": quantiles(selected_series("duration_ms")),
            "travel_surface": quantiles(selected_series("travel_surface")),
            "peak_surface_s": quantiles(selected_series("peak_surface_s")),
            "terminal_surface_s": quantiles(selected_series("terminal_surface_s")),
            "terminal_ratio": quantiles(selected_series("terminal_ratio")),
        }

    settled = [item for item in metrics if item["terminal_ratio"] < 0.2]
    released = [item for item in metrics if item["terminal_ratio"] >= 0.8]
    controlled = [
        item for item in metrics if 0.2 <= item["terminal_ratio"] < 0.8
    ]

    sample_step = max(1, len(metrics) // 1200)
    return {
        "gestures": len(gestures),
        "duration_ms": quantiles(series("duration_ms")),
        "travel_surface": quantiles(series("travel_surface")),
        "peak_surface_s": quantiles(series("peak_surface_s")),
        "terminal_surface_s": quantiles(series("terminal_surface_s")),
        "terminal_ratio": quantiles(series("terminal_ratio")),
        "kinematic_profiles": {
            "settled": profile("settled", settled),
            "controlled_release": profile("controlled_release", controlled),
            "released_with_momentum": profile("released_with_momentum", released),
        },
        "terminal_ratio_histogram": {
            "edges": [0, 0.1, 0.2, 0.4, 0.6, 0.8, 1.01, 4],
            "counts": histogram(
                series("terminal_ratio"),
                [0, 0.1, 0.2, 0.4, 0.6, 0.8, 1.01, 4],
            ),
        },
        "peak_surface_s_histogram": {
            "edges": [0, 0.25, 0.5, 1, 2, 3, 4, 6, 20],
            "counts": histogram(
                series("peak_surface_s"),
                [0, 0.25, 0.5, 1, 2, 3, 4, 6, 20],
            ),
        },
        "scatter": [
            [
                round(item["duration_ms"], 1),
                round(item["peak_surface_s"], 4),
                round(item["terminal_surface_s"], 4),
                round(item["displacement_surface"], 4),
                round(item["terminal_ratio"], 4),
            ]
            for item in metrics[::sample_step][:1200]
        ],
    }


def summarize_wheel(analysis: CaptureAnalysis) -> dict[str, object]:
    bursts = analysis.bursts
    sample_step = max(1, len(bursts) // 1200)
    active_gaps = [gap for gap in analysis.wheel_gaps if gap <= 250]
    delta_categories = {
        "fine": 0,
        "one_detent": 0,
        "batched_detents": 0,
        "other": 0,
    }
    for delta, count in analysis.wheel_deltas.items():
        magnitude = abs(delta)
        if magnitude < 120:
            delta_categories["fine"] += count
        elif magnitude == 120:
            delta_categories["one_detent"] += count
        elif magnitude % 120 == 0:
            delta_categories["batched_detents"] += count
        else:
            delta_categories["other"] += count
    return {
        "events": analysis.wheel_events,
        "delta_counts": {str(key): value for key, value in sorted(analysis.wheel_deltas.items())},
        "device_events": dict(analysis.wheel_device_events.most_common()),
        "source_events": dict(analysis.wheel_sources.most_common()),
        "injected_events": analysis.wheel_injected,
        "gap_ms": quantiles(analysis.wheel_gaps),
        "active_gap_ms": quantiles(active_gaps),
        "active_gap_histogram": {
            "edges": [0, 5, 8, 12, 16, 24, 32, 48, 75, 120, 180, 251],
            "counts": histogram(
                active_gaps,
                [0, 5, 8, 12, 16, 24, 32, 48, 75, 120, 180, 251],
            ),
        },
        "delta_categories": delta_categories,
        "bursts": len(bursts),
        "burst_count": quantiles([float(burst.count) for burst in bursts]),
        "burst_duration_ms": quantiles([burst.duration_ms for burst in bursts]),
        "burst_median_gap_ms": quantiles([burst.median_gap_ms for burst in bursts]),
        "onset": {
            "first_two_gaps": summarize_wheel_onset(bursts, gap_count=2),
            "first_three_gaps": summarize_wheel_onset(bursts, gap_count=3),
        },
        "scatter": [
            [
                round(burst.duration_ms, 1),
                burst.count,
                round(burst.median_gap_ms, 1),
                round(abs(burst.steps), 2),
                round(burst.terminal_gap_ms, 1),
                burst.device_key,
            ]
            for burst in bursts[::sample_step][:1200]
        ],
    }


def summarize_wheel_onset(
    bursts: list[WheelBurst],
    gap_count: int,
) -> dict[str, object]:
    eligible = [burst for burst in bursts if len(burst.gaps_ms) >= gap_count]
    sustained = [
        burst
        for burst in eligible
        if burst.count >= 12 and burst.median_gap_ms <= 16
    ]

    def onset_gap(burst: WheelBurst) -> float:
        return median(burst.gaps_ms[:gap_count])

    cutoffs = []
    for threshold in [8, 12, 16, 24, 32, 45, 68]:
        selected = [burst for burst in eligible if onset_gap(burst) <= threshold]
        true_positive = sum(
            burst.count >= 12 and burst.median_gap_ms <= 16
            for burst in selected
        )
        cutoffs.append(
            {
                "maximum_gap_ms": threshold,
                "selected": len(selected),
                "precision": round(true_positive / max(len(selected), 1), 4),
                "recall": round(true_positive / max(len(sustained), 1), 4),
            }
        )

    bands = []
    for lower, upper in [(0, 8), (8, 16), (16, 24), (24, 45), (45, math.inf)]:
        selected = [
            burst
            for burst in eligible
            if lower < onset_gap(burst) <= upper
        ]
        bands.append(
            {
                "gap_ms": [lower, None if math.isinf(upper) else upper],
                "bursts": len(selected),
                "burst_count": quantiles([float(burst.count) for burst in selected]),
            }
        )

    return {
        "gaps_observed": gap_count,
        "bursts": len(eligible),
        "sustained_proxy": "count >= 12 and median gap <= 16 ms",
        "cutoffs": cutoffs,
        "cadence_bands": bands,
    }


def main() -> None:
    args = parse_args()
    trackpad_files = select_files(
        args.data_root / "telemetry" / "trackpad",
        "trackpad-frames-*.jsonl",
        args.trackpad_limit,
        args.minimum_age_seconds,
    )
    wheel_files = select_files(
        args.data_root / "telemetry" / "mouse-wheel",
        "wheel-events-*.jsonl",
        args.wheel_limit,
        args.minimum_age_seconds,
    )
    analysis = analyze_captures(trackpad_files, wheel_files, args.burst_gap_ms)
    report = {
        "schema": 1,
        "inputs": {
            "trackpad_files": analysis.trackpad_files,
            "trackpad_frames": analysis.trackpad_frames,
            "wheel_files": analysis.wheel_files,
            "wheel_events": analysis.wheel_events,
            "invalid_rows": analysis.invalid_rows,
        },
        "trackpad": {
            "physical": summarize_gestures(analysis, synthetic=False),
            "synthetic": summarize_gestures(analysis, synthetic=True),
        },
        "wheel": summarize_wheel(analysis),
        "devices": [
            {
                "key": extent.key,
                "synthetic": extent.synthetic,
                "declared_y": [extent.declared_y_min, extent.declared_y_max],
                "observed_y": [extent.observed_y_min, extent.observed_y_max],
                "normalization_range": extent.y_range,
            }
            for extent in analysis.devices.values()
        ],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report["inputs"], indent=2))
    print(f"Report: {args.output.resolve()}")


if __name__ == "__main__":
    main()
