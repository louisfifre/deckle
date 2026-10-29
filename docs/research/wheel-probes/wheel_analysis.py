#!/usr/bin/env python3
r"""Offline analysis of Deckle mouse-wheel capture files (wheel-events-*.jsonl).

Usage:
  python wheel_analysis.py FILE_OR_GLOB [...] [--split-gap SECONDS] [--detail] [--out PATH]
  python wheel_analysis.py --sessions SESSIONS.JSONL [FILE_OR_GLOB_OR_DIR ...] [--out PATH]

Read-only on the captures. Standard library only.

--sessions mode: SESSIONS.JSONL has one JSON object per line written by
wheel-session.ps1 ({label, title, start, end, connection?, optionsSmooth?,
precisionScroll, note}, start/end are ISO 8601 with offset). For each session the
events whose absolute time falls in [start - 0.5 s, end + 0.5 s] are taken from
every capture file that overlaps that window, and the usual statistics are
reported per session, plus a cross-session summary table. With no path, the
default telemetry folder %LOCALAPPDATA%\Deckle\telemetry\mouse-wheel is used.
Absolute time of an event = the file header's wall-clock `started` + the event's
monotonic `t`. The two clocks are not the same one, so a drift of a few hundred
ms is possible over long recordings (hence the 0.5 s margin).

Per scope (each file, each segment when --split-gap is given, and the whole corpus):
  1. devices, 2. counts per (device, src, axis, inj), 3. delta profile per group,
  4. cadence (gap) per group, 5. patterns a-d with examples.

Conventions
  - A "device label" is vid:pid in hex ("0000:0000" = the '(mouse hook)' pseudo-device),
    because device indices are per file and not comparable across files.
  - 120-multiple = non-zero delta with d % 120 == 0. Fractional = non-zero, not a multiple.
  - Cadence stats use the recorded `gap` (ms since previous event of the same device),
    excluding the first event of each device in the file/segment (gap is meaningless there),
    negative gaps (hook timestamps are not strictly monotonic; 2548 such events in the corpus)
    and gaps > 1000 ms.
  - Pattern a: src=hook, inj=0, axis=v, 120-multiple (what Precision Scrolling CanConvert
    would accept, minus the modifier-key condition, which is not recorded).
  - Pattern b: an `a` event with a fractional hook event (any axis, any inj) within 200 ms
    in the same file/segment (either side).
  - Pattern c: any inj=1 event.
  - Pattern d: src=raw events whose delta is fractional.
  - Missing keys in old files: `dev` defaults to 0, `inj` to 0 (reported in the schema).
"""
import argparse
import bisect
import glob
import json
import os
import re
import sys
from collections import Counter, defaultdict
from datetime import datetime, timedelta, timezone

FULL_KEYS = {"t", "dev", "src", "axis", "d", "gap", "inj"}
NEAR_MS = 200.0
GAP_CAP_MS = 1000.0
MAX_EXAMPLES = 5


class Ev:
    __slots__ = ("file", "t", "label", "src", "axis", "d", "gap", "inj", "first", "tags", "note",
                 "idx", "abs")

    def __init__(self, file, t, label, src, axis, d, gap, inj):
        self.file = file
        self.t = t
        self.label = label
        self.src = src
        self.axis = axis
        self.d = d
        self.gap = gap
        self.inj = inj
        self.first = False
        self.tags = ""
        self.note = ""
        self.idx = 0      # raw device index as written in the file (per-file numbering)
        self.abs = None   # absolute epoch seconds, only set in --sessions mode


def short_file(path):
    n = os.path.basename(path)
    n = re.sub(r"^wheel-events-", "", n)
    return re.sub(r"\.jsonl$", "", n)


def hexlabel(vid, pid):
    return "%04X:%04X" % (vid, pid)


def short_devname(name, vid, pid):
    if name == "(mouse hook)":
        return "(mouse hook)"
    m = re.search(r"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", name or "")
    parts = []
    if m:
        parts.append("%s:%s" % (m.group(1).upper(), m.group(2).upper()))
    else:
        parts.append(hexlabel(vid, pid))
    mi = re.search(r"MI_[0-9A-Fa-f]{2}", name or "")
    if mi:
        parts.append(mi.group(0))
    col = re.search(r"Col\d+", name or "", re.I)
    if col:
        parts.append(col.group(0))
    if not m:
        parts.append((name or "")[:48])
    return " ".join(parts)


def load(path):
    """Return dict(file, started, devices{idx:(label,short)}, events, schema, status, bad)."""
    info = {"path": path, "file": short_file(path), "started": "", "devices": {}, "events": [],
            "keys": set(), "has_session": False, "has_device": False, "bad": 0, "status": "ok",
            "raw_dev_hdr": {}}
    rows = []
    try:
        with open(path, "r", encoding="utf-8-sig", errors="replace") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    o = json.loads(line)
                except ValueError:
                    info["bad"] += 1
                    continue
                if not isinstance(o, dict):
                    info["bad"] += 1
                    continue
                ty = o.get("type")
                if ty == "session":
                    info["has_session"] = True
                    info["started"] = str(o.get("started", ""))
                elif ty == "device":
                    info["has_device"] = True
                    idx = o.get("dev", 0)
                    vid = int(o.get("vid", 0) or 0)
                    pid = int(o.get("pid", 0) or 0)
                    name = o.get("name", "")
                    lab = "(hook)" if name == "(mouse hook)" else hexlabel(vid, pid)
                    info["devices"][idx] = (lab, short_devname(name, vid, pid))
                elif "d" in o and "t" in o:
                    info["keys"].update(o.keys())
                    rows.append(o)
                else:
                    info["bad"] += 1
    except OSError as e:
        info["status"] = "unreadable: %s" % e
        return info
    for o in rows:
        idx = o.get("dev", 0)
        label = info["devices"].get(idx, (None,))[0]
        src = o.get("src", "?")
        if label is None:
            label = "dev%s?" % idx
        d = o.get("d", 0)
        if isinstance(d, float) and d == int(d):
            d = int(d)
        e = Ev(info["file"], float(o["t"]), label, src, o.get("axis", "?"), d,
               float(o.get("gap", 0.0) or 0.0), int(o.get("inj", 0) or 0))
        e.idx = idx  # raw dev idx, used for first-per-device flagging
        info["events"].append(e)
    if not info["events"]:
        info["status"] = "empty" if not (info["has_session"] or info["has_device"]) else "header-only"
    return info


def schema_of(info):
    if not info["events"]:
        return "none(%s)" % info["status"]
    missing = sorted(FULL_KEYS - info["keys"])
    hdr = ("S" if info["has_session"] else "-") + ("D" if info["has_device"] else "-")
    if hdr == "SD" and not missing:
        return "full"
    s = "hdr:" + hdr
    if missing:
        s += " missing:" + ",".join(missing)
    return s


def make_units(info, split_gap_s):
    """Split a loaded file into units (the file, or its segments)."""
    evs = info["events"]
    if not evs:
        return []
    groups = [evs]
    if split_gap_s is not None:
        groups = []
        cur = [evs[0]]
        for e in evs[1:]:
            if e.t - cur[-1].t > split_gap_s * 1000.0:
                groups.append(cur)
                cur = []
            cur.append(e)
        groups.append(cur)
    units = []
    for i, g in enumerate(groups):
        seen = set()
        for e in g:
            e.first = e.idx not in seen
            seen.add(e.idx)
            e.note = ""
        tag_events(g)
        name = info["file"] if split_gap_s is None else "%s#%d" % (info["file"], i + 1)
        if split_gap_s is not None:
            name += " [t=%.1f..%.1fs, %d ev]" % (g[0].t / 1000, g[-1].t / 1000, len(g))
        units.append((name, g))
    return units


def is_mult(d):
    return d != 0 and d % 120 == 0


def is_frac(d):
    return d != 0 and d % 120 != 0


def tag_events(g):
    frac_hook_t = []
    frac_hook_d = []
    for e in g:
        if e.src == "hook" and is_frac(e.d):
            frac_hook_t.append(e.t)
            frac_hook_d.append(e.d)
    for e in g:
        tags = ""
        if e.src == "hook" and e.inj == 0 and e.axis == "v" and is_mult(e.d):
            tags += "a"
            if frac_hook_t:
                i = bisect.bisect_left(frac_hook_t, e.t)
                best = None
                for j in (i - 1, i):
                    if 0 <= j < len(frac_hook_t):
                        off = frac_hook_t[j] - e.t
                        if abs(off) <= NEAR_MS and (best is None or abs(off) < abs(best[0])):
                            best = (off, frac_hook_d[j])
                if best:
                    tags += "b"
                    e.note = "nearest frac hook d=%d at %+.1fms" % (best[1], best[0])
        if e.inj == 1:
            tags += "c"
        if e.src == "raw" and is_frac(e.d):
            tags += "d"
        e.tags = tags


def pct(sorted_vals, p):
    if not sorted_vals:
        return None
    if len(sorted_vals) == 1:
        return sorted_vals[0]
    k = (len(sorted_vals) - 1) * p
    lo = int(k)
    hi = min(lo + 1, len(sorted_vals) - 1)
    return sorted_vals[lo] + (sorted_vals[hi] - sorted_vals[lo]) * (k - lo)


def fmt(v):
    return "-" if v is None else ("%.1f" % v)


def group_lines(events, indent="  "):
    groups = defaultdict(list)
    for e in events:
        groups[(e.label, e.src, e.axis, e.inj)].append(e)
    out = []
    for key in sorted(groups):
        g = groups[key]
        n = len(g)
        mult = sum(1 for e in g if is_mult(e.d))
        frac = sum(1 for e in g if is_frac(e.d))
        zero = sum(1 for e in g if e.d == 0)
        ds = [e.d for e in g]
        top = Counter(abs(d) for d in ds).most_common(10)
        gaps = sorted(e.gap for e in g if not e.first and 0 <= e.gap <= GAP_CAP_MS)
        label, src, axis, inj = key
        out.append("%s%s %-4s %s inj=%d  n=%d  x120=%.1f%% frac=%.1f%%%s  d[min,max]=[%d,%d]" % (
            indent, label, src, axis, inj, n, 100.0 * mult / n, 100.0 * frac / n,
            (" zero=%d" % zero) if zero else "", min(ds), max(ds)))
        out.append("%s    top|d|: %s" % (indent, ", ".join("%s x%d" % (v, c) for v, c in top)))
        out.append("%s    gap ms med/p10/p90: %s/%s/%s (n=%d, first-per-device, negative and >%d ms excluded)" % (
            indent, fmt(pct(gaps, 0.5)), fmt(pct(gaps, 0.1)), fmt(pct(gaps, 0.9)), len(gaps), GAP_CAP_MS))
        if mult and frac:  # mixed group: also profile each kind separately
            for kind, pred in (("x120", is_mult), ("frac", is_frac)):
                sub = [e for e in g if pred(e.d)]
                sg = sorted(e.gap for e in sub if not e.first and 0 <= e.gap <= GAP_CAP_MS)
                st = Counter(abs(e.d) for e in sub).most_common(10)
                out.append("%s    [%s only] n=%d gap med/p10/p90: %s/%s/%s (n=%d); top|d|: %s" % (
                    indent, kind, len(sub), fmt(pct(sg, 0.5)), fmt(pct(sg, 0.1)), fmt(pct(sg, 0.9)), len(sg),
                    ", ".join("%s x%d" % kv for kv in st)))
    return out


PATTERN_TITLES = {
    "a": "a. hook, inj=0, axis=v, |d| multiple of 120 (CanConvert-eligible)",
    "b": "b. a-event with a fractional hook event within %d ms" % NEAR_MS,
    "c": "c. inj=1 events",
    "d": "d. raw events with fractional delta",
}


def pattern_lines(events, indent="  "):
    out = []
    for p in "abcd":
        hit = [e for e in events if p in e.tags]
        out.append("%s%s: %d" % (indent, PATTERN_TITLES[p], len(hit)))
        if p in "cd" and hit:
            by = Counter((e.label, e.src, e.axis, e.inj) for e in hit)
            out.append("%s    by (dev,src,axis,inj): %s" % (
                indent, "; ".join("%s %s %s inj=%d: %d" % (k + (c,)) for k, c in sorted(by.items()))))
        if p == "b":
            fh = sum(1 for e in events if e.src == "hook" and is_frac(e.d))
            out.append("%s    (fractional hook events in scope: %d)" % (indent, fh))
        # spread examples across files rather than taking only the first file
        seen_files = []
        ex = []
        for e in hit:
            if e.file not in seen_files:
                seen_files.append(e.file)
                ex.append(e)
        for e in hit:
            if len(ex) >= MAX_EXAMPLES:
                break
            if e not in ex:
                ex.append(e)
        ex = sorted(ex[:MAX_EXAMPLES], key=lambda e: (e.file, e.t))
        for e in ex:
            out.append("%s    e.g. %s t=%.1f %s %s %s inj=%d d=%d gap=%.1f%s" % (
                indent, e.file, e.t, e.label, e.src, e.axis, e.inj, e.d, e.gap,
                (" [" + e.note + "]") if e.note else ""))
    return out


def counts_by(events):
    c = Counter((e.src, e.inj) for e in events)
    return "h0=%d h1=%d r0=%d r1=%d" % (c[("hook", 0)], c[("hook", 1)], c[("raw", 0)], c[("raw", 1)])


def pct120(events):
    if not events:
        return "-"
    return "%.1f%%" % (100.0 * sum(1 for e in events if is_mult(e.d)) / len(events))


def summary_line(name, schema, info, events):
    labels = sorted({e.label for e in events})
    hook = [e for e in events if e.src == "hook"]
    raw = [e for e in events if e.src == "raw"]
    pc = Counter(p for e in events for p in e.tags)
    return "%s | %s | %s | n=%d | %s | x120 all=%s hook=%s raw=%s | a=%d b=%d c=%d d=%d" % (
        name, schema, ",".join(labels) or "-", len(events), counts_by(events),
        pct120(events), pct120(hook), pct120(raw), pc["a"], pc["b"], pc["c"], pc["d"])


def detail_block(name, info, events, indent="  "):
    out = ["== %s ==" % name]
    out.append("%sdevices (idx -> vid:pid name): %s" % (indent, "; ".join(
        "%s -> %s %s" % (i, l, s) for i, (l, s) in sorted(info["devices"].items())) or "(none in header)"))
    out.append("%sevents: %d  (%s)" % (indent, len(events), counts_by(events)))
    out += group_lines(events, indent)
    out += pattern_lines(events, indent)
    return out


# ---------------------------------------------------------------------------
# --sessions mode
# ---------------------------------------------------------------------------

WINDOW_MARGIN_S = 0.5
DEFAULT_TELEMETRY_DIR = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Deckle", "telemetry", "mouse-wheel")
_ISO_RE = re.compile(r"^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:[.,](\d+))?\s*(Z|[+-]\d{2}(?::?\d{2})?)?$")


def parse_iso(text):
    """Parse an ISO 8601 timestamp (any number of fractional digits, offset optional) to an aware datetime.

    Done by hand because datetime.fromisoformat only accepts 7-digit fractions
    (what PowerShell's "o" format writes) from Python 3.11 on.
    A timestamp without offset is read as local time.
    """
    m = _ISO_RE.match(str(text).strip())
    if not m:
        return None
    y, mo, d, h, mi, s = (int(m.group(i)) for i in range(1, 7))
    micro = int((m.group(7) or "")[:6].ljust(6, "0"))
    tz = m.group(8)
    try:
        if tz is None:
            return datetime(y, mo, d, h, mi, s, micro).astimezone()
        if tz == "Z":
            return datetime(y, mo, d, h, mi, s, micro, tzinfo=timezone.utc)
        sign = -1 if tz[0] == "-" else 1
        digits = tz[1:].replace(":", "")
        off = timedelta(hours=int(digits[:2]), minutes=int(digits[2:4] or 0))
        return datetime(y, mo, d, h, mi, s, micro, tzinfo=timezone(sign * off))
    except ValueError:
        return None


def fmt_local(ts):
    return datetime.fromtimestamp(ts).astimezone().strftime("%Y-%m-%d %H:%M:%S")


def expand_paths(paths):
    """Files, globs and directories (-> their wheel-events-*.jsonl) to a sorted list of files."""
    files = []
    for p in paths:
        if os.path.isdir(p):
            files += glob.glob(os.path.join(p, "wheel-events-*.jsonl"))
            continue
        hits = glob.glob(p)
        files += hits if hits else ([p] if os.path.exists(p) else [])
    return sorted(set(files))


def peek_started(path):
    """Epoch seconds of the file's `session` header, or None if unreadable."""
    try:
        with open(path, "r", encoding="utf-8-sig", errors="replace") as fh:
            for _ in range(5):
                line = fh.readline()
                if not line:
                    break
                try:
                    o = json.loads(line)
                except ValueError:
                    continue
                if isinstance(o, dict) and o.get("type") == "session":
                    dt = parse_iso(o.get("started", ""))
                    return dt.timestamp() if dt else None
    except OSError:
        pass
    return None


def read_sessions(path, warnings):
    """Read sessions.jsonl; returns a list of dicts with start_ts/end_ts added."""
    sessions = []
    with open(path, "r", encoding="utf-8-sig") as fh:
        for n, line in enumerate(fh, 1):
            line = line.strip()
            if not line:
                continue
            try:
                o = json.loads(line)
            except ValueError:
                warnings.append("sessions line %d: not valid JSON, skipped" % n)
                continue
            s_dt = parse_iso(o.get("start", "")) if isinstance(o, dict) else None
            e_dt = parse_iso(o.get("end", "")) if isinstance(o, dict) else None
            if s_dt is None or e_dt is None:
                warnings.append("sessions line %d: start/end missing or unparsable, skipped" % n)
                continue
            if e_dt < s_dt:
                warnings.append("sessions line %d (%s): end before start, skipped" % (n, o.get("label", "?")))
                continue
            o["start_ts"] = s_dt.timestamp()
            o["end_ts"] = e_dt.timestamp()
            o["_line"] = n
            sessions.append(o)
    return sessions


def session_name(s):
    return "%s @ %s" % (s.get("label", "?"), fmt_local(s["start_ts"])[5:16])


def pick_session_events(infos, s):
    """Events of every loaded file whose absolute time lies in the session window (+- margin)."""
    lo = s["start_ts"] - WINDOW_MARGIN_S
    hi = s["end_ts"] + WINDOW_MARGIN_S
    picked = []
    used = []
    for info in infos:
        base = info.get("started_ts")
        if base is None or not info["events"]:
            continue
        hit = []
        for e in info["events"]:
            a = base + e.t / 1000.0
            if lo <= a <= hi:
                e.abs = a
                hit.append(e)
        if hit:
            used.append(info)
            picked.append((info, hit))
    return picked, used


def prepare_session_events(picked):
    """Flag first-per-device events and tag patterns a-d, file by file (t is per-file)."""
    events = []
    devices = {}
    for info, hit in picked:
        seen = set()
        for e in hit:
            e.first = e.idx not in seen
            seen.add(e.idx)
            e.note = ""
            dev = info["devices"].get(e.idx)
            if dev:
                devices["%s:%s" % (info["file"], e.idx)] = dev
        tag_events(hit)
        events += hit
    return events, devices


def median_gap(events):
    gaps = sorted(e.gap for e in events if not e.first and 0 <= e.gap <= GAP_CAP_MS)
    return pct(gaps, 0.5)


def pct_of(events, pred):
    if not events:
        return "-"
    return "%.0f%%" % (100.0 * sum(1 for e in events if pred(e)) / len(events))


def sessions_report(args):
    warnings = []
    try:
        sessions = read_sessions(args.sessions, warnings)
    except OSError as ex:
        print("cannot read sessions file: %s" % ex, file=sys.stderr)
        return None
    if not sessions:
        print("no usable session in %s" % args.sessions, file=sys.stderr)
        return None

    files = expand_paths(args.paths or [DEFAULT_TELEMETRY_DIR])
    if not files:
        print("no capture file found", file=sys.stderr)
        return None

    # Cheap prefilter so the whole telemetry folder (hundreds of files) is not parsed:
    # a file is relevant only if it started before the last window ended, and was last
    # written after the first window began (events are flushed before the mtime).
    min_lo = min(s["start_ts"] for s in sessions) - WINDOW_MARGIN_S
    max_hi = max(s["end_ts"] for s in sessions) + WINDOW_MARGIN_S
    infos = []
    for f in files:
        started = peek_started(f)
        if started is not None:
            if started > max_hi:
                continue
            try:
                if os.path.getmtime(f) < min_lo - 5.0:
                    continue
            except OSError:
                pass
        info = load(f)
        info["started_ts"] = started
        if started is None and info["events"]:
            warnings.append("%s: no usable `started` header, cannot be placed in time, ignored" % info["file"])
        infos.append(info)

    lines = []
    lines.append("# Wheel sessions report, generated %s" % fmt_local(datetime.now().timestamp()))
    lines.append("# sessions file: %s  (%d sessions)" % (args.sessions, len(sessions)))
    lines.append("# capture files scanned=%d, parsed after time prefilter=%d" % (len(files), len(infos)))
    lines.append("# Window per session = [start - %.1f s, end + %.1f s]; an event is kept when "
                 "its absolute time falls inside." % (WINDOW_MARGIN_S, WINDOW_MARGIN_S))
    lines.append("# NOTE: absolute event time = wall-clock `started` of the file header + monotonic `t` of the event. "
                 "The two clocks differ, so a drift of a few hundred ms is possible over long recordings.")
    lines.append("# NOTE: Deckle flushes a live file every 500 ms of event time and on stop; the tail of a file "
                 "still being recorded may be missing. Turn the recorder off before analysing.")
    for w in warnings:
        lines.append("# WARNING: " + w)
    lines.append("")

    results = []  # (session, events, devices, used_files)
    for s in sorted(sessions, key=lambda x: x["start_ts"]):
        picked, used = pick_session_events(infos, s)
        events, devices = prepare_session_events(picked)
        results.append((s, events, devices, used))

    # ---- cross-session table
    lines.append("## Cross-session summary")
    header = ("%-3s %-22s %-13s %7s %6s %6s %6s %7s %9s  %s" %
              ("no", "session", "label", "events", "hook", "raw", "inj", "x120", "med gap", "devices"))
    lines.append(header)
    lines.append("-" * len(header))
    for i, (s, evs, _devs, _used) in enumerate(results, 1):
        mg = median_gap(evs)
        labels = ",".join(sorted({e.label for e in evs})) or "-"
        lines.append("%-3d %-22s %-13s %7d %6s %6s %6s %7s %9s  %s" % (
            i, fmt_local(s["start_ts"])[5:], str(s.get("label", "?"))[:13], len(evs),
            pct_of(evs, lambda e: e.src == "hook"), pct_of(evs, lambda e: e.src == "raw"),
            pct_of(evs, lambda e: e.inj == 1), pct_of(evs, lambda e: is_mult(e.d)),
            ("%.1f ms" % mg) if mg is not None else "-", labels))
    lines.append("(hook/raw/inj = share of events; x120 = share of events whose delta is a non-zero multiple of 120; "
                 "med gap = median recorded gap, first-per-device, negative and >%d ms excluded)" % GAP_CAP_MS)
    lines.append("")

    # ---- per session
    for i, (s, evs, devices, used) in enumerate(results, 1):
        lines.append("=" * 78)
        lines.append("## Session %d: %s - %s" % (i, s.get("label", "?"), s.get("title", "")))
        lines.append("  window: %s .. %s  (%.1f s)" % (
            fmt_local(s["start_ts"]), fmt_local(s["end_ts"]), s["end_ts"] - s["start_ts"]))
        meta = []
        for key, name in (("connection", "connection"), ("optionsSmooth", "Options+ smooth scrolling"),
                          ("precisionScroll", "Precision Scrolling"), ("note", "note")):
            if key in s and s[key] not in (None, ""):
                meta.append("%s=%s" % (name, s[key]))
        lines.append("  metadata: " + ("; ".join(meta) if meta else "(none)"))
        lines.append("  files used: %s" % (", ".join(u["file"] for u in used) or "(none)"))
        if not evs:
            lines.append("  NO EVENTS in this window. Possible causes: recording was off, the file tail was not "
                         "flushed yet (turn the recorder off), or the clocks drifted more than the margin.")
            lines.append("")
            continue
        lines.append("  devices (file:idx -> vid:pid name): %s" % "; ".join(
            "%s -> %s %s" % (k, l, n) for k, (l, n) in sorted(devices.items())))
        lines.append("  events: %d  (%s)" % (len(evs), counts_by(evs)))
        lines.append("  first event at %s, last at %s (offset from window start: %+.2f s / %+.2f s)" % (
            fmt_local(min(e.abs for e in evs)), fmt_local(max(e.abs for e in evs)),
            min(e.abs for e in evs) - s["start_ts"], max(e.abs for e in evs) - s["start_ts"]))
        lines += group_lines(evs)
        lines += pattern_lines(evs)
        lines.append("")
    return "\n".join(lines)


def emit(text, out):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    print(text)
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="*", help="files or globs (with --sessions: also folders; default: telemetry folder)")
    ap.add_argument("--sessions", metavar="PATH",
                    help="sessions.jsonl written by wheel-session.ps1: report per labelled session window")
    ap.add_argument("--split-gap", type=float, default=None, metavar="SECONDS",
                    help="cut each file into segments at pauses longer than this")
    ap.add_argument("--detail", action="store_true",
                    help="full report for every file/segment (default: only when 3 or fewer units)")
    ap.add_argument("--out", help="also write the output (UTF-8) to this path")
    args = ap.parse_args()

    if args.sessions:
        text = sessions_report(args)
        return 1 if text is None else emit(text, args.out)
    if not args.paths:
        ap.error("give at least one file or glob, or use --sessions")

    files = expand_paths(args.paths)
    if not files:
        print("no files matched", file=sys.stderr)
        return 1

    lines = []
    infos = [load(f) for f in files]
    all_units = []  # (name, schema, info, events)
    for info in infos:
        sch = schema_of(info)
        info["schema"] = sch
        for name, evs in make_units(info, args.split_gap):
            all_units.append((name, sch, info, evs))
    all_events = [e for _, _, _, evs in all_units for e in evs]

    # ---- corpus header
    lines.append("# files=%d units=%d events=%d split_gap=%s" % (
        len(files), len(all_units), len(all_events), args.split_gap))
    status = Counter(i["status"] for i in infos)
    lines.append("# file status: " + ", ".join("%s=%d" % kv for kv in sorted(status.items())))
    bad = sum(i["bad"] for i in infos)
    if bad:
        lines.append("# unparsable/unknown lines skipped: %d" % bad)
    lines.append("")
    lines.append("## Schemas (header lines S=session D=device; missing event keys)")
    by_schema = defaultdict(list)
    for i in infos:
        by_schema[i["schema"]].append(i)
    for sch, lst in sorted(by_schema.items(), key=lambda kv: -len(kv[1])):
        dates = sorted(re.sub(r"T.*", "", i["started"]) or i["file"][:8] for i in lst)
        lines.append("  %-40s files=%d  %s..%s  e.g. %s" % (
            sch, len(lst), dates[0], dates[-1], lst[0]["file"]))
    lines.append("")

    # ---- devices aggregated
    lines.append("## Devices (distinct vid:pid + short name; files = files where seen)")
    dev_files = defaultdict(set)
    dev_events = Counter()
    for info in infos:
        for idx, (l, s) in info["devices"].items():
            dev_files[(l, s)].add(info["file"])
    for _, _, info, evs in all_units:
        pass
    for k in sorted(dev_files):
        lines.append("  %s  [%s]  files=%d" % (k[0], k[1], len(dev_files[k])))
    lines.append("  (index -> device mapping is per file; see per-file detail)")
    lines.append("")

    # ---- aggregate
    lines += detail_block("AGGREGATE (all units)", {"devices": {}}, all_events)[0:1]
    lines += ["  events: %d  (%s)" % (len(all_events), counts_by(all_events))]
    lines += group_lines(all_events)
    lines += pattern_lines(all_events)
    lines.append("")

    # ---- per unit
    detail = args.detail or len(all_units) <= 3
    lines.append("## Per %s" % ("segment" if args.split_gap is not None else "file")
                 + " (name | schema | device labels | n | counts hook/raw x inj | %120 | pattern counts)")
    for name, sch, info, evs in all_units:
        if detail:
            lines.append(summary_line(name, sch, info, evs))
            lines += detail_block(name, info, evs)
            lines.append("")
        else:
            lines.append(summary_line(name, sch, info, evs))
    for info in infos:
        if not info["events"]:
            lines.append("%s | %s | (%s)" % (info["file"], info["schema"], info["status"]))

    return emit("\n".join(lines), args.out)


if __name__ == "__main__":
    sys.exit(main())


