from pathlib import Path
from tempfile import TemporaryDirectory
import json
import os
import time
import unittest

from capture_analysis import (
    CaptureAnalysis,
    analyze_trackpad_file,
    analyze_wheel_file,
    histogram,
    scan_delta_ms,
)
from analyze import select_files, summarize_wheel


class CaptureAnalysisTests(unittest.TestCase):
    def test_scan_clock_wrap_preserves_frame_interval(self) -> None:
        self.assertAlmostEqual(11.0, scan_delta_ms(65_500, 74, 0.2))

    def test_two_finger_scroll_becomes_one_gesture(self) -> None:
        rows = [
            {"type": "device", "name": "physical", "vid": 1, "pid": 2, "x": [0, 1000], "y": [0, 1000]},
            frame(0, 100, 800),
            frame(10, 200, 700),
            frame(20, 300, 600),
            frame(30, 400, 500),
            {"t": 40, "scan": 500, "n": 2, "tips": 0, "c": [[1, 300, 500, 0, 1], [2, 700, 500, 0, 1]]},
        ]
        with TemporaryDirectory() as directory:
            path = Path(directory) / "trackpad.jsonl"
            write_rows(path, rows)
            analysis = CaptureAnalysis()
            analyze_trackpad_file(path, analysis)

        self.assertEqual(1, len(analysis.gestures))
        gesture = analysis.gestures[0]
        self.assertTrue(gesture.is_scroll_candidate)
        self.assertEqual(-300, gesture.displacement_y)

    def test_wheel_direction_change_splits_burst(self) -> None:
        rows = [
            {"t": 0, "axis": "v", "d": 120, "gap": 0},
            {"t": 10, "axis": "v", "d": 120, "gap": 10},
            {"t": 20, "axis": "v", "d": -120, "gap": 10},
        ]
        with TemporaryDirectory() as directory:
            path = Path(directory) / "wheel.jsonl"
            write_rows(path, rows)
            analysis = CaptureAnalysis()
            analyze_wheel_file(path, analysis, burst_gap_ms=250)

        self.assertEqual([2, 1], [burst.count for burst in analysis.bursts])

    def test_interleaved_devices_keep_independent_bursts(self) -> None:
        rows = [
            {"type": "device", "dev": 0, "name": "first", "vid": 1, "pid": 1},
            {"type": "device", "dev": 1, "name": "second", "vid": 2, "pid": 2},
            {"t": 0, "dev": 0, "src": "raw", "axis": "v", "d": 120, "gap": 0},
            {"t": 5, "dev": 1, "src": "raw", "axis": "v", "d": -120, "gap": 0},
            {"t": 10, "dev": 0, "src": "raw", "axis": "v", "d": 120, "gap": 10},
        ]
        with TemporaryDirectory() as directory:
            path = Path(directory) / "wheel.jsonl"
            write_rows(path, rows)
            analysis = CaptureAnalysis()
            analyze_wheel_file(path, analysis, burst_gap_ms=250)

        self.assertEqual([1, 2], sorted(burst.count for burst in analysis.bursts))
        self.assertEqual({"raw": 3}, dict(analysis.wheel_sources))

    def test_truncated_row_does_not_discard_valid_capture(self) -> None:
        rows = [
            '{"t":0,"axis":"v","d":120,"gap":0}\n',
            '{"t":10,"axis":"v"\n',
            '{"t":20,"axis":"v","d":120,"gap":20}\n',
        ]
        with TemporaryDirectory() as directory:
            path = Path(directory) / "wheel.jsonl"
            path.write_text("".join(rows), encoding="utf-8")
            analysis = CaptureAnalysis()
            analyze_wheel_file(path, analysis, burst_gap_ms=250)

        self.assertEqual(1, analysis.invalid_rows)
        self.assertEqual(2, analysis.wheel_events)

    def test_wheel_report_measures_causal_onset_evidence(self) -> None:
        rows = [
            {"t": index * 10, "axis": "v", "d": 120, "gap": 10 if index else 0}
            for index in range(13)
        ]
        rows.extend(
            {"t": 500 + index * 80, "axis": "v", "d": 120, "gap": 80 if index else 380}
            for index in range(4)
        )
        with TemporaryDirectory() as directory:
            path = Path(directory) / "wheel.jsonl"
            write_rows(path, rows)
            analysis = CaptureAnalysis()
            analyze_wheel_file(path, analysis, burst_gap_ms=250)

        report = summarize_wheel(analysis)
        first_three = report["onset"]["first_three_gaps"]
        cutoff = next(
            item for item in first_three["cutoffs"] if item["maximum_gap_ms"] == 16
        )

        self.assertEqual(2, first_three["bursts"])
        self.assertEqual(1.0, cutoff["precision"])
        self.assertEqual(1.0, cutoff["recall"])

    def test_histogram_uses_half_open_bins(self) -> None:
        self.assertEqual([2, 2], histogram([0, 0.9, 1, 1.9], [0, 1, 2]))

    def test_file_selection_excludes_a_capture_still_being_written(self) -> None:
        with TemporaryDirectory() as directory:
            root = Path(directory)
            closed = root / "trackpad-frames-closed.jsonl"
            live = root / "trackpad-frames-live.jsonl"
            closed.touch()
            live.touch()
            now = time.time()
            os.utime(closed, (now - 120, now - 120))

            selected = select_files(
                root,
                "trackpad-frames-*.jsonl",
                limit=0,
                minimum_age_seconds=60,
            )

        self.assertEqual([closed.name], [path.name for path in selected])


def frame(t: int, scan: int, y: int) -> dict[str, object]:
    return {
        "t": t,
        "scan": scan,
        "n": 2,
        "tips": 2,
        "c": [[1, 300, y, 1, 1], [2, 700, y, 1, 1]],
    }


def write_rows(path: Path, rows: list[dict[str, object]]) -> None:
    path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
