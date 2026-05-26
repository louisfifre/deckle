"""Génère un HTML autonome de comparaison qualitative.

Pour chaque sample du run voxtral-validation :
- un lecteur audio HTML5
- les transcriptions Gemini GT / Whisper sticky / Voxtral T1/T2/T6 côte à côte
- les régimes de transformation T3/T4/T5 en collapsible
- les verdicts judge Gemini en collapsible
- une zone de notes libre persistée en localStorage par audio_id

Tri : par tier dans l'ordre very-short → short → medium → long → edge,
puis par durée croissante à l'intérieur d'un tier.
"""

from __future__ import annotations

import argparse
import html
import json
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve()
BENCHMARK_DIR = HERE.parents[2]

FIDELITY_REGIMES = ["T1_baseline", "T2_verbatim", "T6_sys_prompt"]
TRANSFORM_REGIMES = ["T3_translate", "T4_summary", "T5_qa_register"]
ALL_REGIMES = FIDELITY_REGIMES + TRANSFORM_REGIMES

TIER_ORDER = ["very-short", "short", "medium", "long", "edge"]

REGIME_LABEL = {
    "T1_baseline": "Voxtral T1 baseline",
    "T2_verbatim": "Voxtral T2 verbatim",
    "T6_sys_prompt": "Voxtral T6 sys_prompt",
    "T3_translate": "Voxtral T3 translate",
    "T4_summary": "Voxtral T4 summary",
    "T5_qa_register": "Voxtral T5 qa_register",
}


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        "--corpus",
        type=Path,
        default=BENCHMARK_DIR / "corpora" / "voxtral-val-30" / "corpus.jsonl",
    )
    p.add_argument(
        "--results",
        type=Path,
        required=True,
        help="results.jsonl d'un run voxtral-validation",
    )
    p.add_argument(
        "--out",
        type=Path,
        default=None,
        help="HTML de sortie (par défaut : comparison.html à côté de --results)",
    )
    return p.parse_args()


def load_corpus(path: Path) -> dict[str, dict]:
    by_id: dict[str, dict] = {}
    with path.open(encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            p = row["payload"]
            by_id[p["transcription_id"]] = {
                "audio_file": p["audio_file"],
                "tier": p["tier"],
                "duration_s": p["duration_seconds"],
                "reference_whisper": p.get("text", ""),
                "reference_gemini": p.get("reference_text_gemini", ""),
                "sticky_prompt": p.get("prompt_or_instruction", ""),
            }
    return by_id


def load_results(path: Path) -> dict[tuple[str, str], dict]:
    out: dict[tuple[str, str], dict] = {}
    with path.open(encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            r = json.loads(line)
            out[(r["audio_id"], r["regime"])] = r
    return out


def fmt_duration(s: float) -> str:
    if s < 60:
        return f"{s:.1f}s"
    m = int(s // 60)
    rest = s - 60 * m
    return f"{m}m{rest:04.1f}s"


def cell_text(text: str) -> str:
    if not text:
        return '<span class="empty">(vide)</span>'
    return html.escape(text).replace("\n", "<br>")


def render_fidelity_table(corpus_row: dict, results_for_audio: dict[str, dict]) -> str:
    rows = [
        ("ref-gemini", "Gemini GT", corpus_row["reference_gemini"]),
        ("ref-whisper", "Whisper large-v3 (sticky)", corpus_row["reference_whisper"]),
    ]
    for regime in FIDELITY_REGIMES:
        r = results_for_audio.get(regime)
        text = r["text"] if r else ""
        rows.append((f"voxtral {regime}", REGIME_LABEL[regime], text))
    html_rows = []
    for css_cls, label, text in rows:
        html_rows.append(
            f'<tr class="row-{css_cls}">'
            f'<th scope="row">{html.escape(label)}</th>'
            f"<td>{cell_text(text)}</td>"
            f"</tr>"
        )
    return '<table class="transcriptions"><tbody>' + "".join(html_rows) + "</tbody></table>"


def render_transformations(results_for_audio: dict[str, dict]) -> str:
    html_rows = []
    for regime in TRANSFORM_REGIMES:
        r = results_for_audio.get(regime)
        text = r["text"] if r else ""
        html_rows.append(
            f'<tr class="row-voxtral {regime}">'
            f'<th scope="row">{html.escape(REGIME_LABEL[regime])}</th>'
            f"<td>{cell_text(text)}</td>"
            f"</tr>"
        )
    return (
        "<details><summary>Transformations (T3 translate, T4 summary, T5 qa)</summary>"
        '<table class="transcriptions"><tbody>'
        + "".join(html_rows)
        + "</tbody></table></details>"
    )


def render_verdicts(results_for_audio: dict[str, dict]) -> str:
    html_rows = []
    for regime in ALL_REGIMES:
        r = results_for_audio.get(regime)
        if not r:
            continue
        judge = r.get("judge") or {}
        axes = judge.get("axes") or {}
        verdict = judge.get("verdict") or ""
        score_cells = "".join(
            f"<td>{axes.get(k, '—')}</td>"
            for k in ("fidelite_signal", "proprete", "absence_hallucination", "regime_respecte")
        )
        html_rows.append(
            f"<tr>"
            f"<th scope='row'>{html.escape(REGIME_LABEL[regime])}</th>"
            f"{score_cells}"
            f"<td class='verdict-prose'>{html.escape(verdict)}</td>"
            f"</tr>"
        )
    return (
        "<details><summary>Verdicts Gemini par régime (4 axes 0–100 + verdict prose)</summary>"
        '<table class="verdicts"><thead><tr>'
        "<th></th>"
        "<th>fidélité signal</th><th>propreté</th>"
        "<th>absence halluc.</th><th>régime respecté</th>"
        "<th>verdict</th>"
        "</tr></thead><tbody>"
        + "".join(html_rows)
        + "</tbody></table></details>"
    )


def render_sample(audio_id: str, corpus_row: dict, results: dict, audio_rel: str) -> str:
    short_id = audio_id[:8]
    results_for_audio = {
        regime: results.get((audio_id, regime))
        for regime in ALL_REGIMES
        if (audio_id, regime) in results
    }
    return (
        f'<article class="sample" id="s-{short_id}" data-audio-id="{audio_id}">'
        f'  <header>'
        f'    <span class="tier-chip tier-{corpus_row["tier"]}">{corpus_row["tier"]}</span>'
        f'    <span class="duration">{fmt_duration(corpus_row["duration_s"])}</span>'
        f'    <code class="audio-id">{short_id}</code>'
        f"  </header>"
        f'  <audio controls preload="none" src="{html.escape(audio_rel)}"></audio>'
        f"  {render_fidelity_table(corpus_row, results_for_audio)}"
        f"  {render_transformations(results_for_audio)}"
        f"  {render_verdicts(results_for_audio)}"
        f'  <label class="note-label" for="note-{short_id}">Tes notes (sauvées en local)</label>'
        f'  <textarea class="user-note" id="note-{short_id}" data-audio-id="{audio_id}" rows="3" '
        f'placeholder="Tes impressions à l\'écoute…"></textarea>'
        f"</article>"
    )


def main() -> None:
    args = parse_args()
    out_path = args.out or args.results.parent / "comparison.html"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    corpus = load_corpus(args.corpus)
    results = load_results(args.results)

    audio_dir = args.corpus.parent.resolve()
    rel_audio_dir = Path(
        *([".."] * (len(out_path.resolve().parent.parts) - len(BENCHMARK_DIR.resolve().parts)))
    ) / audio_dir.relative_to(BENCHMARK_DIR.resolve())

    by_tier: dict[str, list[str]] = defaultdict(list)
    for audio_id, row in corpus.items():
        by_tier[row["tier"]].append(audio_id)
    for tier in by_tier:
        by_tier[tier].sort(key=lambda aid: corpus[aid]["duration_s"])

    tiers_in_order = [t for t in TIER_ORDER if t in by_tier] + [
        t for t in by_tier if t not in TIER_ORDER
    ]

    toc_links = []
    sections_html = []
    for tier in tiers_in_order:
        ids = by_tier[tier]
        durations = [corpus[aid]["duration_s"] for aid in ids]
        dmin, dmax = min(durations), max(durations)
        toc_links.append(
            f'<a href="#tier-{tier}">'
            f'<span class="tier-chip tier-{tier}">{tier}</span>'
            f"<span class='count'>{len(ids)}</span></a>"
        )

        sample_blocks = []
        for aid in ids:
            audio_file = corpus[aid]["audio_file"]
            audio_rel = (rel_audio_dir / audio_file).as_posix()
            sample_blocks.append(render_sample(aid, corpus[aid], results, audio_rel))

        sections_html.append(
            f'<section class="tier" id="tier-{tier}">'
            f'<h2><span class="tier-chip tier-{tier}">{tier}</span> '
            f"{len(ids)} samples · {fmt_duration(dmin)} → {fmt_duration(dmax)}</h2>"
            + "".join(sample_blocks)
            + "</section>"
        )

    total = sum(len(v) for v in by_tier.values())
    run_label = args.results.parent.name

    page = f"""<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<title>Comparaison qualitative — {html.escape(run_label)}</title>
<style>
:root {{
  color-scheme: light dark;
  --bg: #fafafa;
  --fg: #1a1a1a;
  --muted: #666;
  --border: #d0d0d0;
  --row-ref-gemini-bg: #e8f5e9;
  --row-ref-whisper-bg: #fff3e0;
  --row-voxtral-bg: #e3f2fd;
  --chip-very-short: #fce4ec;
  --chip-short: #e1bee7;
  --chip-medium: #b3e5fc;
  --chip-long: #c8e6c9;
  --chip-edge: #ffe082;
  --accent: #1976d2;
}}
@media (prefers-color-scheme: dark) {{
  :root {{
    --bg: #1a1a1a;
    --fg: #e8e8e8;
    --muted: #999;
    --border: #3a3a3a;
    --row-ref-gemini-bg: #1b3a23;
    --row-ref-whisper-bg: #3d2a14;
    --row-voxtral-bg: #142b3d;
    --chip-very-short: #5a2c3e;
    --chip-short: #4a2c5a;
    --chip-medium: #1c4a64;
    --chip-long: #2a4a2c;
    --chip-edge: #5a4214;
    --accent: #64b5f6;
  }}
}}
body {{
  font-family: "Segoe UI Variable", "Segoe UI", system-ui, sans-serif;
  background: var(--bg);
  color: var(--fg);
  max-width: 1100px;
  margin: 0 auto;
  padding: 1rem 1.5rem 4rem;
  line-height: 1.5;
}}
h1 {{ margin-top: 0.5rem; }}
h1, h2 {{ font-weight: 600; }}
header.page {{
  border-bottom: 1px solid var(--border);
  padding-bottom: 1rem;
  margin-bottom: 1.5rem;
}}
.meta {{ color: var(--muted); font-size: 0.9rem; }}
nav.toc {{
  position: sticky;
  top: 0;
  background: var(--bg);
  padding: 0.6rem 0;
  border-bottom: 1px solid var(--border);
  z-index: 10;
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
}}
nav.toc a {{
  text-decoration: none;
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  color: var(--fg);
}}
nav.toc .count {{ color: var(--muted); font-size: 0.85rem; }}
.tier-chip {{
  display: inline-block;
  padding: 0.1rem 0.5rem;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 500;
  letter-spacing: 0.02em;
}}
.tier-very-short {{ background: var(--chip-very-short); }}
.tier-short {{ background: var(--chip-short); }}
.tier-medium {{ background: var(--chip-medium); }}
.tier-long {{ background: var(--chip-long); }}
.tier-edge {{ background: var(--chip-edge); }}
section.tier {{ margin-top: 2.5rem; }}
section.tier h2 {{ display: flex; align-items: center; gap: 0.5rem; }}
article.sample {{
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 1rem 1.2rem;
  margin: 1.2rem 0;
  background: color-mix(in srgb, var(--bg) 95%, var(--fg) 5%);
}}
article.sample > header {{
  display: flex;
  align-items: center;
  gap: 0.7rem;
  margin-bottom: 0.6rem;
}}
.duration {{ color: var(--muted); font-variant-numeric: tabular-nums; }}
.audio-id {{ color: var(--muted); font-size: 0.85rem; }}
audio {{ width: 100%; margin: 0.4rem 0 0.8rem; }}
table.transcriptions, table.verdicts {{
  width: 100%;
  border-collapse: collapse;
  margin: 0.3rem 0;
  font-size: 0.95rem;
}}
table.transcriptions th, table.transcriptions td,
table.verdicts th, table.verdicts td {{
  border: 1px solid var(--border);
  padding: 0.5rem 0.7rem;
  vertical-align: top;
  text-align: left;
}}
table.transcriptions th {{
  width: 200px;
  white-space: nowrap;
  font-weight: 500;
  background: color-mix(in srgb, var(--bg) 80%, var(--fg) 20%);
}}
table.transcriptions tr.row-ref-gemini td {{ background: var(--row-ref-gemini-bg); }}
table.transcriptions tr.row-ref-whisper td {{ background: var(--row-ref-whisper-bg); }}
table.transcriptions tr.row-voxtral td,
table.transcriptions tr.T3_translate td,
table.transcriptions tr.T4_summary td,
table.transcriptions tr.T5_qa_register td {{ background: var(--row-voxtral-bg); }}
.empty {{ color: var(--muted); font-style: italic; }}
details {{
  margin: 0.6rem 0;
}}
details summary {{
  cursor: pointer;
  color: var(--accent);
  padding: 0.3rem 0;
  font-weight: 500;
}}
table.verdicts th {{
  background: color-mix(in srgb, var(--bg) 80%, var(--fg) 20%);
  font-weight: 500;
  font-size: 0.85rem;
}}
table.verdicts td {{
  font-variant-numeric: tabular-nums;
  text-align: center;
}}
table.verdicts td.verdict-prose {{
  text-align: left;
  font-size: 0.9rem;
  color: var(--muted);
}}
.note-label {{
  display: block;
  margin-top: 0.8rem;
  margin-bottom: 0.25rem;
  font-size: 0.85rem;
  color: var(--muted);
}}
textarea.user-note {{
  width: 100%;
  font-family: inherit;
  font-size: 0.95rem;
  padding: 0.5rem 0.7rem;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg);
  color: var(--fg);
  resize: vertical;
}}
textarea.user-note:focus {{ outline: 2px solid var(--accent); outline-offset: 1px; }}
.toolbar {{
  position: fixed;
  bottom: 1rem;
  right: 1rem;
  display: flex;
  gap: 0.5rem;
  background: var(--bg);
  padding: 0.5rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  box-shadow: 0 2px 12px rgba(0,0,0,0.15);
}}
.toolbar button {{
  font-family: inherit;
  font-size: 0.85rem;
  padding: 0.4rem 0.8rem;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg);
  color: var(--fg);
  cursor: pointer;
}}
.toolbar button:hover {{ border-color: var(--accent); color: var(--accent); }}
.toolbar .status {{ color: var(--muted); font-size: 0.8rem; align-self: center; }}
</style>
</head>
<body>
<header class="page">
  <h1>Comparaison qualitative — Voxtral 24B Q4_K_M vs Whisper vs Gemini</h1>
  <p class="meta">
    Run <code>{html.escape(run_label)}</code> · corpus <code>voxtral-val-30</code>
    · {total} samples · régimes fidélité T1/T2/T6, transformations T3/T4/T5
  </p>
</header>
<nav class="toc">
  <span class="meta">Aller au tier :</span>
  {''.join(toc_links)}
</nav>
{''.join(sections_html)}
<div class="toolbar">
  <span class="status" id="note-status">notes sauvées localement</span>
  <button id="export-notes">Exporter JSON</button>
  <button id="copy-notes">Copier presse-papier</button>
  <button id="clear-notes">Effacer toutes</button>
</div>
<script>
(function () {{
  const STORAGE_KEY = 'voxtral-val-30/notes';
  const status = document.getElementById('note-status');

  function loadAll() {{
    try {{ return JSON.parse(localStorage.getItem(STORAGE_KEY) || '{{}}'); }}
    catch (e) {{ return {{}}; }}
  }}
  function saveAll(d) {{
    localStorage.setItem(STORAGE_KEY, JSON.stringify(d));
    status.textContent = `${{Object.keys(d).filter(k => d[k]).length}} note(s) sauvées localement`;
  }}

  const all = loadAll();
  document.querySelectorAll('textarea.user-note').forEach(ta => {{
    const id = ta.dataset.audioId;
    if (all[id]) ta.value = all[id];
    ta.addEventListener('input', () => {{
      const data = loadAll();
      if (ta.value.trim()) data[id] = ta.value;
      else delete data[id];
      saveAll(data);
    }});
  }});
  saveAll(loadAll()); // refresh count

  document.getElementById('export-notes').addEventListener('click', () => {{
    const data = loadAll();
    const blob = new Blob([JSON.stringify(data, null, 2)], {{type: 'application/json'}});
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'voxtral-val-30-notes.json';
    a.click();
    URL.revokeObjectURL(a.href);
  }});
  document.getElementById('copy-notes').addEventListener('click', async () => {{
    const data = loadAll();
    await navigator.clipboard.writeText(JSON.stringify(data, null, 2));
    status.textContent = 'notes copiées dans le presse-papier';
  }});
  document.getElementById('clear-notes').addEventListener('click', () => {{
    if (!confirm('Effacer toutes les notes ?')) return;
    localStorage.removeItem(STORAGE_KEY);
    document.querySelectorAll('textarea.user-note').forEach(ta => ta.value = '');
    status.textContent = 'toutes les notes effacées';
  }});
}})();
</script>
</body>
</html>
"""

    out_path.write_text(page, encoding="utf-8")
    print(f"OK {out_path}")
    print(f"  {total} samples sur {len(by_tier)} tiers")
    print(f"  audios -> {rel_audio_dir.as_posix()}/")


if __name__ == "__main__":
    main()
