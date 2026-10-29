"""Build the dashboard's two CSV files from the labelled wheel sessions of 2026-10-29."""
import csv, json, os, statistics, datetime as dt

ROOT = os.path.join(os.environ["LOCALAPPDATA"], "Deckle", "telemetry", "mouse-wheel")
CAPTURE = os.path.join(ROOT, "wheel-events-20261029-131052.jsonl")
SESSIONS = os.path.join(ROOT, "sessions", "sessions.jsonl")
OUT = os.path.dirname(os.path.abspath(__file__))
KEEP = {"S1": "S1 · MX, PS off", "S3": "S3 · MX, PS on", "S4": "S4 · touchpad, PS off", "S6": "S6 · Steam, PS off"}
WINDOW_MS = 100.0

lines = open(CAPTURE, encoding="utf-8-sig").read().splitlines()
start = dt.datetime.fromisoformat(json.loads(lines[0])["started"])
events = [json.loads(l) for l in lines[1:] if l.startswith('{"t"')]
sessions = [json.loads(l) for l in open(SESSIONS, encoding="utf-8-sig") if l.strip()]


def kind(label, e):
    x120 = e["d"] != 0 and e["d"] % 120 == 0
    if label == "S1": return "MX"
    if label == "S3": return "MX" if x120 else "rejeu Deckle"
    if label == "S4": return "touchpad"
    if label == "S6": return "Steam" if e["inj"] else "touchpad"


rows, summary = [], []
for s in sessions:
    label = s["label"]
    if label not in KEEP: continue
    lo = dt.datetime.fromisoformat(s["start"]) - dt.timedelta(seconds=0.5)
    hi = dt.datetime.fromisoformat(s["end"]) + dt.timedelta(seconds=0.5)
    picked = [e for e in events if lo <= start + dt.timedelta(milliseconds=e["t"]) <= hi]
    if not picked: continue
    t0 = picked[0]["t"]
    per_kind = {}
    for e in picked:
        k = kind(label, e)
        per_kind.setdefault(k, []).append(e)
    for k, evs in per_kind.items():
        cum, prev_t, window = 0.0, None, []
        for e in evs:
            t = e["t"] - t0
            cum += e["d"] / 120.0
            window.append((t, e["d"]))
            while window and window[0][0] <= t - WINDOW_MS: window.pop(0)
            speed = sum(d for _, d in window) / 120.0 / (WINDOW_MS / 1000.0)
            gap = None if prev_t is None else round(t - prev_t, 1)
            prev_t = t
            rows.append({"session": KEEP[label], "source": k, "t_s": round(t / 1000.0, 4),
                         "delta": e["d"], "notches": round(e["d"] / 120.0, 4),
                         "position_notches": round(cum, 3), "speed_nps": round(speed, 2),
                         "gap_ms": "" if gap is None else gap, "injected": e["inj"]})
        gaps = sorted(r for r in (
            (b["t"] - a["t"]) for a, b in zip(evs, evs[1:])) if 0 < r <= 1000)
        q = statistics.quantiles(gaps, n=10) if len(gaps) >= 10 else [None] * 9
        summary.append({"session": KEEP[label], "source": k, "events": len(evs),
                        "share_x120_pct": round(100 * sum(1 for e in evs if e["d"] % 120 == 0) / len(evs), 1),
                        "share_injected_pct": round(100 * sum(e["inj"] for e in evs) / len(evs), 1),
                        "gap_median_ms": round(statistics.median(gaps), 1) if gaps else "",
                        "gap_p10_ms": round(q[0], 1) if q[0] is not None else "",
                        "gap_p90_ms": round(q[8], 1) if q[8] is not None else "",
                        "max_abs_delta": max(abs(e["d"]) for e in evs),
                        "total_notches": round(sum(abs(e["d"]) for e in evs) / 120.0, 1)})

summary_key = [f"{r['session']} | {r['source']}" for r in summary]
for r, key in zip(summary, summary_key): r["row"] = key

with open(os.path.join(OUT, "wheel_events_20261029.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
with open(os.path.join(OUT, "wheel_summary_20261029.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=["row"] + [k for k in summary[0] if k != "row"]); w.writeheader(); w.writerows(summary)
print(len(rows), "event rows;", len(summary), "summary rows")
for r in summary: print(r)
