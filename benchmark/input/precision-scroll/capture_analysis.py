from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from statistics import median
from typing import Iterable
import json
import math


@dataclass(slots=True)
class DeviceExtent:
    key: str
    synthetic: bool
    declared_y_min: int
    declared_y_max: int
    observed_y_min: int = 2**31 - 1
    observed_y_max: int = -(2**31)

    def observe(self, y: float) -> None:
        self.observed_y_min = min(self.observed_y_min, int(y))
        self.observed_y_max = max(self.observed_y_max, int(y))

    @property
    def y_range(self) -> int:
        declared = self.declared_y_max - self.declared_y_min
        if (
            declared > 0
            and self.observed_y_min >= self.declared_y_min
            and self.observed_y_max <= self.declared_y_max
        ):
            return declared
        return max(1, self.observed_y_max - self.observed_y_min)


@dataclass(slots=True)
class TrackpadGesture:
    device_key: str
    synthetic: bool
    duration_ms: float
    frame_count: int
    displacement_x: float
    displacement_y: float
    path_x: float
    path_y: float
    peak_speed_y: float
    median_speed_y: float
    terminal_speed_y: float
    separation_cv: float

    @property
    def verticality(self) -> float:
        return self.path_y / max(self.path_x + self.path_y, 1e-9)

    @property
    def direction_consistency(self) -> float:
        return abs(self.displacement_y) / max(self.path_y, 1e-9)

    @property
    def is_scroll_candidate(self) -> bool:
        return (
            self.frame_count >= 4
            and self.duration_ms >= 20
            and self.path_y > 0
            and self.verticality >= 0.65
            and self.direction_consistency >= 0.60
            and self.separation_cv <= 0.35
        )


@dataclass(slots=True)
class _GestureBuilder:
    device_key: str
    synthetic: bool
    contact_ids: tuple[int, int]
    first_t: float
    last_t: float
    last_scan: int
    first_x: float
    first_y: float
    last_x: float
    last_y: float
    frame_count: int = 1
    path_x: float = 0
    path_y: float = 0
    speeds_y: list[float] = field(default_factory=list)
    separations: list[float] = field(default_factory=list)

    def add(
        self,
        t: float,
        scan: int,
        first: tuple[int, int, int],
        second: tuple[int, int, int],
    ) -> bool:
        ids = tuple(sorted((first[0], second[0])))
        if ids != self.contact_ids:
            return False

        x = (first[1] + second[1]) / 2
        y = (first[2] + second[2]) / 2
        host_dt = t - self.last_t
        dt = scan_delta_ms(self.last_scan, scan, host_dt)
        if dt <= 0 or dt > 80:
            return False

        dx = x - self.last_x
        dy = y - self.last_y
        self.path_x += abs(dx)
        self.path_y += abs(dy)
        self.speeds_y.append(abs(dy) / dt)
        self.separations.append(math.hypot(first[1] - second[1], first[2] - second[2]))
        self.last_t = t
        self.last_scan = scan
        self.last_x = x
        self.last_y = y
        self.frame_count += 1
        return True

    def finish(self) -> TrackpadGesture:
        speeds = self.speeds_y or [0.0]
        terminal = median(speeds[-min(3, len(speeds)):])
        separation_mean = sum(self.separations) / max(len(self.separations), 1)
        separation_variance = (
            sum((value - separation_mean) ** 2 for value in self.separations)
            / max(len(self.separations), 1)
        )
        separation_cv = math.sqrt(separation_variance) / max(separation_mean, 1e-9)
        return TrackpadGesture(
            device_key=self.device_key,
            synthetic=self.synthetic,
            duration_ms=max(0.0, self.last_t - self.first_t),
            frame_count=self.frame_count,
            displacement_x=self.last_x - self.first_x,
            displacement_y=self.last_y - self.first_y,
            path_x=self.path_x,
            path_y=self.path_y,
            peak_speed_y=percentile(speeds, 0.95),
            median_speed_y=median(speeds),
            terminal_speed_y=terminal,
            separation_cv=separation_cv,
        )


@dataclass(slots=True)
class WheelBurst:
    device_key: str
    count: int
    duration_ms: float
    steps: float
    median_gap_ms: float
    minimum_gap_ms: float
    terminal_gap_ms: float
    direction: int
    gaps_ms: tuple[float, ...]


@dataclass(slots=True)
class CaptureAnalysis:
    trackpad_files: int = 0
    wheel_files: int = 0
    trackpad_frames: int = 0
    wheel_events: int = 0
    invalid_rows: int = 0
    devices: dict[str, DeviceExtent] = field(default_factory=dict)
    gestures: list[TrackpadGesture] = field(default_factory=list)
    bursts: list[WheelBurst] = field(default_factory=list)
    wheel_deltas: Counter[int] = field(default_factory=Counter)
    wheel_device_events: Counter[str] = field(default_factory=Counter)
    wheel_sources: Counter[str] = field(default_factory=Counter)
    wheel_injected: int = 0
    wheel_gaps: list[float] = field(default_factory=list)


def scan_delta_ms(previous: int, current: int, host_delta_ms: float) -> float:
    delta = current - previous
    if delta < 0:
        wrapped = delta + 65_536
        if 0 < wrapped <= 800:
            delta = wrapped
        else:
            return host_delta_ms
    device_ms = delta * 0.1
    return device_ms if 0 < device_ms <= 80 else host_delta_ms


def analyze_trackpad_file(path: Path, analysis: CaptureAnalysis) -> None:
    device: DeviceExtent | None = None
    builder: _GestureBuilder | None = None
    analysis.trackpad_files += 1

    with path.open("r", encoding="utf-8-sig") as stream:
        for line in stream:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                analysis.invalid_rows += 1
                continue
            if row.get("type") == "device":
                name = str(row.get("name", "unknown"))
                y_range = row.get("y", [0, 0])
                synthetic = "Microsoft HID RID\\000D_0005" in name
                key = f"{row.get('vid', 0):04x}:{row.get('pid', 0):04x}:{name}"
                device = analysis.devices.setdefault(
                    key,
                    DeviceExtent(
                        key=key,
                        synthetic=synthetic,
                        declared_y_min=int(y_range[0]),
                        declared_y_max=int(y_range[1]),
                    ),
                )
                continue
            if "t" not in row or device is None:
                continue

            analysis.trackpad_frames += 1
            active = [
                (int(contact[0]), int(contact[1]), int(contact[2]))
                for contact in row.get("c", [])
                if int(contact[3]) == 1 and int(contact[4]) == 1
            ]
            for _, _, y in active:
                device.observe(y)

            if len(active) != 2:
                if builder is not None:
                    analysis.gestures.append(builder.finish())
                    builder = None
                continue

            active.sort(key=lambda contact: contact[0])
            first, second = active
            t = float(row["t"])
            scan = int(row.get("scan", 0))
            centroid_x = (first[1] + second[1]) / 2
            centroid_y = (first[2] + second[2]) / 2

            if builder is not None and builder.add(t, scan, first, second):
                continue
            if builder is not None:
                analysis.gestures.append(builder.finish())

            builder = _GestureBuilder(
                device_key=device.key,
                synthetic=device.synthetic,
                contact_ids=(first[0], second[0]),
                first_t=t,
                last_t=t,
                last_scan=scan,
                first_x=centroid_x,
                first_y=centroid_y,
                last_x=centroid_x,
                last_y=centroid_y,
                separations=[math.hypot(first[1] - second[1], first[2] - second[2])],
            )

    if builder is not None:
        analysis.gestures.append(builder.finish())


def analyze_wheel_file(path: Path, analysis: CaptureAnalysis, burst_gap_ms: float) -> None:
    devices: dict[int, str] = {}
    currents: dict[str, list[tuple[float, int, float]]] = {}
    current_directions: dict[str, int] = {}
    analysis.wheel_files += 1

    def finish(device_key: str) -> None:
        current = currents.get(device_key, [])
        if not current:
            return
        times = [item[0] for item in current]
        gaps = [item[2] for item in current[1:] if item[2] > 0]
        analysis.bursts.append(
            WheelBurst(
                device_key=device_key,
                count=len(current),
                duration_ms=max(times) - min(times),
                steps=sum(item[1] for item in current) / 120,
                median_gap_ms=median(gaps) if gaps else 0,
                minimum_gap_ms=min(gaps) if gaps else 0,
                terminal_gap_ms=median(gaps[-min(3, len(gaps)):]) if gaps else 0,
                direction=current_directions[device_key],
                gaps_ms=tuple(gaps),
            )
        )
        currents[device_key] = []

    with path.open("r", encoding="utf-8-sig") as stream:
        for line in stream:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                analysis.invalid_rows += 1
                continue
            if row.get("type") == "device":
                device_index = int(row.get("dev", len(devices)))
                name = str(row.get("name", "(unknown)"))
                vid = int(row.get("vid", 0))
                pid = int(row.get("pid", 0))
                devices[device_index] = f"{vid:04x}:{pid:04x}:{name}"
                continue
            if "t" not in row or row.get("axis") != "v":
                continue
            delta = int(row.get("d", 0))
            if delta == 0:
                continue
            gap = float(row.get("gap", 0))
            direction = 1 if delta > 0 else -1
            device_index = int(row.get("dev", 0))
            device_key = devices.get(device_index, f"session:{path.name}:dev:{device_index}")
            current = currents.setdefault(device_key, [])
            analysis.wheel_events += 1
            analysis.wheel_deltas[delta] += 1
            analysis.wheel_device_events[device_key] += 1
            analysis.wheel_sources[str(row.get("src", "unknown"))] += 1
            analysis.wheel_injected += int(bool(row.get("inj", False)))
            if gap > 0:
                analysis.wheel_gaps.append(gap)

            if current and (
                direction != current_directions[device_key] or gap > burst_gap_ms
            ):
                finish(device_key)
                current = currents[device_key]
            if not current:
                current_directions[device_key] = direction
            current.append((float(row["t"]), delta, gap))
    for device_key in currents:
        finish(device_key)


def analyze_captures(
    trackpad_files: Iterable[Path],
    wheel_files: Iterable[Path],
    burst_gap_ms: float = 250,
) -> CaptureAnalysis:
    analysis = CaptureAnalysis()
    for path in trackpad_files:
        analyze_trackpad_file(path, analysis)
    for path in wheel_files:
        analyze_wheel_file(path, analysis, burst_gap_ms)
    return analysis


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    lower = int(math.floor(position))
    upper = int(math.ceil(position))
    if lower == upper:
        return ordered[lower]
    weight = position - lower
    return ordered[lower] * (1 - weight) + ordered[upper] * weight


def quantiles(values: list[float]) -> dict[str, float]:
    return {
        "p10": round(percentile(values, 0.10), 4),
        "p25": round(percentile(values, 0.25), 4),
        "p50": round(percentile(values, 0.50), 4),
        "p75": round(percentile(values, 0.75), 4),
        "p90": round(percentile(values, 0.90), 4),
        "p95": round(percentile(values, 0.95), 4),
        "p99": round(percentile(values, 0.99), 4),
    }


def histogram(values: list[float], edges: list[float]) -> list[int]:
    counts = [0] * (len(edges) - 1)
    for value in values:
        for index in range(len(edges) - 1):
            if edges[index] <= value < edges[index + 1]:
                counts[index] += 1
                break
    return counts
