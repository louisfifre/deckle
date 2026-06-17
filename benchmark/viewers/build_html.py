"""Viewer HTML générique de comparaison qualitative.

Pour n'importe quel run de bench respectant le format normalisé Deckle :

  - ``corpus.jsonl`` (sous ``CORPORA_DIR/<slug>/``) : layout v2,
    ``payload.transcription_id``, ``payload.audio_file``, ``payload.tier``,
    ``payload.duration_seconds``, plus toutes les clés ``reference_text_*``
    présentes (auto-découvertes).
  - ``results.jsonl`` (sous ``RUNS_DIR/<run-name>/``) : une row par
    (audio_id, regime), avec ``text``, ``metrics.wer``, ``judge.axes``,
    ``judge.verdict``. Les régimes sont auto-découverts dans l'ordre
    d'apparition.

Produit un HTML autonome avec :
  - audio player HTML5 par sample (path relatif vers le corpus)
  - table des références du corpus et des sorties par régime
  - judge collapsible par sample (axes + verdict prose, si présent)
  - textarea de notes persistées en localStorage par audio_id
  - export JSON des notes (download ou clipboard)

Pas de hardcoding voxtral-validation — l'outil consomme ce qu'il trouve.
Pour overrider les labels lisibles (sinon = identifiants techniques bruts),
passer un mapping JSON via ``--labels``. Format :

    {
      "regimes": {"T1_baseline": "Baseline", "T2_verbatim": "..."},
      "references": {"reference_text_gemini": "Gemini GT", "text": "Deckle"}
    }
"""

from __future__ import annotations

import argparse
import html
import json
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve()
BENCHMARK_CODE_DIR = HERE.parents[1]
import sys

sys.path.insert(0, str(BENCHMARK_CODE_DIR))
from lib import paths  # noqa: E402

# Ordre canonique des tiers Deckle (cf. benchmark/asr/build_corpus.py).
# Tout tier inconnu tombe à la fin, ordre alphabétique.
TIER_ORDER = ["very-short", "short", "medium", "long", "very-long-edge", "edge"]


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--results", type=Path, required=True,
                   help="results.jsonl d'un run (sous RUNS_DIR/<run>/)")
    p.add_argument("--corpus", type=Path, default=None,
                   help="corpus.jsonl (par défaut : déduit depuis le 1er audio_id du run)")
    p.add_argument("--labels", type=Path, default=None,
                   help="JSON {regimes:{}, references:{}} pour overrider les labels")
    p.add_argument("--out", type=Path, default=None,
                   help="HTML de sortie (par défaut : comparison.html à côté de --results)")
    return p.parse_args()


def load_corpus(path: Path) -> tuple[dict[str, dict], list[str]]:
    """Charge le corpus.jsonl, retourne ({audio_id → row}, [reference_keys_découvertes]).

    Une clé est gardée comme référence si elle commence par ``reference_text_``
    et a au moins une valeur non-vide dans le corpus."""
    by_id: dict[str, dict] = {}
    ref_keys_seen: dict[str, bool] = {}  # ordre d'insertion + has_value
    with path.open(encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            p = row["payload"]
            by_id[p["transcription_id"]] = {
                "audio_file": p["audio_file"],
                "tier": p.get("tier", "?"),
                "duration_s": float(p.get("duration_seconds", 0)),
                "payload": p,
            }
            # Convention Deckle telemetry : `text` est la transcription
            # embarquée (Whisper large-v3 dans le pipeline actuel). Les
            # `reference_text_*` sont les références ajoutées par les
            # passes ground-truth. Les deux sont affichées comme références.
            for k in p:
                if k != "text" and not k.startswith("reference_text_"):
                    continue
                has_val = bool(str(p.get(k, "")).strip())
                ref_keys_seen.setdefault(k, False)
                if has_val:
                    ref_keys_seen[k] = True
    refs_with_value = [k for k, v in ref_keys_seen.items() if v]
    # Ordre : gemini ground truth d'abord, puis text Deckle (Whisper),
    # puis les autres reference_text_* par ordre alpha
    def _ref_sort_key(k: str) -> tuple[int, str]:
        if "gemini" in k:
            return (0, k)
        if k == "text":
            return (1, k)
        return (2, k)
    refs_with_value.sort(key=_ref_sort_key)
    return by_id, refs_with_value


def load_results(path: Path) -> tuple[dict[tuple[str, str], dict], list[str]]:
    """Charge results.jsonl, retourne ({(audio_id, regime) → row}, [regimes dans l'ordre d'apparition])."""
    out: dict[tuple[str, str], dict] = {}
    regimes_order: list[str] = []
    seen = set()
    with path.open(encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            r = json.loads(line)
            regime = r["regime"]
            out[(r["audio_id"], regime)] = r
            if regime not in seen:
                seen.add(regime)
                regimes_order.append(regime)
    return out, regimes_order


def resolve_corpus_path(results_path: Path, results: dict, by_id: dict | None) -> Path | None:
    """Si --corpus pas fourni, tente de deviner via paths.corpus_dir et
    le 1er audio_id du run. Renvoie None si introuvable."""
    if not results:
        return None
    # On essaie d'abord par convention : tous les runs Deckle pointent un
    # corpus connu par son slug, qu'on peut tenter de retrouver via le
    # nom du run ou via les valeurs des champs `source_label` / `corpus`.
    # Pour l'instant on tente une heuristique simple : on scanne CORPORA_DIR
    # pour trouver un corpus.jsonl qui contienne au moins un des audio_id.
    sample_ids = list({k[0] for k in results.keys()})[:5]
    if not paths.CORPORA_DIR.exists():
        return None
    for corpus_subdir in paths.CORPORA_DIR.iterdir():
        if not corpus_subdir.is_dir():
            continue
        jsonl = corpus_subdir / "corpus.jsonl"
        if not jsonl.exists():
            continue
        try:
            content = jsonl.read_text(encoding="utf-8")
        except OSError:
            continue
        if any(sid in content for sid in sample_ids):
            return jsonl
    return None


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


def render_transcriptions_table(
    corpus_row: dict,
    results_for_audio: dict[str, dict],
    references: list[str],
    regimes: list[str],
    ref_labels: dict[str, str],
    regime_labels: dict[str, str],
) -> str:
    """Table principale : 1 ligne par référence du corpus + 1 ligne par régime du bench."""
    rows = []
    payload = corpus_row.get("payload", {})
    for ref_key in references:
        text = payload.get(ref_key, "")
        label = ref_labels.get(ref_key, ref_key)
        rows.append((f"row-ref ref-{ref_key.replace('_', '-')}", label, text))
    for regime in regimes:
        r = results_for_audio.get(regime)
        text = r["text"] if r else ""
        label = regime_labels.get(regime, regime)
        rows.append((f"row-regime regime-{regime.replace('_', '-')}", label, text))
    html_rows = []
    for css_cls, label, text in rows:
        html_rows.append(
            f'<tr class="{css_cls}">'
            f'<th scope="row">{html.escape(label)}</th>'
            f"<td>{cell_text(text)}</td>"
            f"</tr>"
        )
    return '<table class="transcriptions"><tbody>' + "".join(html_rows) + "</tbody></table>"


def render_judge_block(
    results_for_audio: dict[str, dict],
    regimes: list[str],
    regime_labels: dict[str, str],
) -> str:
    """Collapsible verdicts judge — affiche tous les axes présents."""
    # Découvre dynamiquement les axes utilisés
    axes_seen: list[str] = []
    seen = set()
    for r in results_for_audio.values():
        for ax in (r.get("judge") or {}).get("axes", {}):
            if ax not in seen and ax != "whisper_ref_suspecte":
                seen.add(ax)
                axes_seen.append(ax)
    if not axes_seen:
        return ""

    html_rows = []
    for regime in regimes:
        r = results_for_audio.get(regime)
        if not r:
            continue
        judge = r.get("judge") or {}
        axes = judge.get("axes") or {}
        verdict = judge.get("verdict") or ""
        score_cells = "".join(f"<td>{axes.get(ax, '—')}</td>" for ax in axes_seen)
        html_rows.append(
            f"<tr>"
            f"<th scope='row'>{html.escape(regime_labels.get(regime, regime))}</th>"
            f"{score_cells}"
            f"<td class='verdict-prose'>{html.escape(verdict)}</td>"
            f"</tr>"
        )
    if not html_rows:
        return ""
    headers = "".join(f"<th>{html.escape(ax.replace('_', ' '))}</th>" for ax in axes_seen)
    return (
        "<details open><summary>Verdicts judge (axes + verdict prose)</summary>"
        '<table class="verdicts"><thead><tr>'
        f"<th></th>{headers}<th>verdict</th>"
        "</tr></thead><tbody>"
        + "".join(html_rows)
        + "</tbody></table></details>"
    )


def render_sample(
    audio_id: str,
    corpus_row: dict,
    results: dict,
    audio_rel: str,
    references: list[str],
    regimes: list[str],
    ref_labels: dict[str, str],
    regime_labels: dict[str, str],
) -> str:
    short_id = audio_id[:8]
    results_for_audio = {
        regime: results.get((audio_id, regime))
        for regime in regimes
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
        + render_transcriptions_table(corpus_row, results_for_audio, references, regimes,
                                       ref_labels, regime_labels)
        + render_judge_block(results_for_audio, regimes, regime_labels)
        + (
            f'  <label class="note-label" for="note-{short_id}">Tes notes (sauvées en local)</label>'
            f'  <textarea class="user-note" id="note-{short_id}" data-audio-id="{audio_id}" rows="3" '
            f'placeholder="Tes impressions à l\'écoute…"></textarea>'
        )
        + "</article>"
    )


def main() -> None:
    args = parse_args()
    out_path = args.out or args.results.parent / "comparison.html"
    out_path.parent.mkdir(parents=True, exist_ok=True)

    results, regimes = load_results(args.results)

    # Localise le corpus
    corpus_path = args.corpus
    if not corpus_path:
        corpus_path = resolve_corpus_path(args.results, results, None)
        if not corpus_path:
            raise SystemExit(
                "FATAL : corpus.jsonl introuvable. Passe-le explicitement via --corpus.\n"
                f"  Cherché sous {paths.CORPORA_DIR} via heuristique sur audio_id, sans résultat."
            )
    corpus, references = load_corpus(corpus_path)
    audio_dir = corpus_path.parent.resolve()

    # Labels — overrides optionnels
    regime_labels: dict[str, str] = {}
    ref_labels: dict[str, str] = {}
    if args.labels and args.labels.exists():
        cfg = json.loads(args.labels.read_text(encoding="utf-8"))
        regime_labels = dict(cfg.get("regimes", {}))
        ref_labels = dict(cfg.get("references", {}))

    # Path audio : on utilise un path absolu file:/// — le HTML peut alors
    # vivre n'importe où sans casser les liens audio.
    def audio_url(audio_file: str) -> str:
        return (audio_dir / audio_file).resolve().as_uri()

    # Regroupe par tier dans l'ordre canonique
    by_tier: dict[str, list[str]] = defaultdict(list)
    for aid, row in corpus.items():
        by_tier[row["tier"]].append(aid)
    for tier in by_tier:
        by_tier[tier].sort(key=lambda aid: corpus[aid]["duration_s"])

    def tier_sort_key(t: str) -> tuple[int, str]:
        if t in TIER_ORDER:
            return (TIER_ORDER.index(t), "")
        return (len(TIER_ORDER), t)

    tiers_in_order = sorted(by_tier.keys(), key=tier_sort_key)

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
            row = corpus[aid]
            sample_blocks.append(
                render_sample(aid, row, results, audio_url(row["audio_file"]),
                              references, regimes, ref_labels, regime_labels)
            )

        sections_html.append(
            f'<section class="tier" id="tier-{tier}">'
            f'<h2><span class="tier-chip tier-{tier}">{tier}</span> '
            f"{len(ids)} samples · {fmt_duration(dmin)} → {fmt_duration(dmax)}</h2>"
            + "".join(sample_blocks)
            + "</section>"
        )

    total = sum(len(v) for v in by_tier.values())
    run_label = args.results.parent.name

    page = _page_template(run_label, total, toc_links, sections_html)
    out_path.write_text(page, encoding="utf-8")
    print(f"OK {out_path}")
    print(f"  {total} samples sur {len(by_tier)} tiers")
    print(f"  regimes : {regimes}")
    print(f"  references : {references}")


def _page_template(run_label: str, total: int, toc_links: list[str], sections_html: list[str]) -> str:
    return f"""<!doctype html>
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
  --row-ref-bg: #e8f5e9;
  --row-regime-bg: #e3f2fd;
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
    --row-ref-bg: #1b3a23;
    --row-regime-bg: #142b3d;
    --chip-very-short: #5a2c3e;
    --chip-short: #4a2c5a;
    --chip-medium: #1c4a64;
    --chip-long: #2a4a2c;
    --chip-edge: #5a4214;
    --accent: #64b5f6;
  }}
}}
body {{ font-family: "Segoe UI Variable", "Segoe UI", system-ui, sans-serif;
       background: var(--bg); color: var(--fg);
       max-width: 1100px; margin: 0 auto; padding: 1rem 1.5rem 4rem; line-height: 1.5; }}
h1, h2 {{ font-weight: 600; }}
h1 {{ margin-top: 0.5rem; }}
header.page {{ border-bottom: 1px solid var(--border); padding-bottom: 1rem; margin-bottom: 1.5rem; }}
.meta {{ color: var(--muted); font-size: 0.9rem; }}
nav.toc {{ position: sticky; top: 0; background: var(--bg); padding: 0.6rem 0;
          border-bottom: 1px solid var(--border); z-index: 10;
          display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }}
nav.toc a {{ text-decoration: none; display: inline-flex; align-items: center;
            gap: 0.3rem; color: var(--fg); }}
nav.toc .count {{ color: var(--muted); font-size: 0.85rem; }}
.tier-chip {{ display: inline-block; padding: 0.1rem 0.5rem; border-radius: 999px;
             font-size: 0.78rem; font-weight: 500; letter-spacing: 0.02em; }}
.tier-very-short {{ background: var(--chip-very-short); }}
.tier-short {{ background: var(--chip-short); }}
.tier-medium {{ background: var(--chip-medium); }}
.tier-long {{ background: var(--chip-long); }}
.tier-very-long-edge, .tier-edge {{ background: var(--chip-edge); }}
section.tier {{ margin-top: 2.5rem; }}
section.tier h2 {{ display: flex; align-items: center; gap: 0.5rem; }}
article.sample {{ border: 1px solid var(--border); border-radius: 8px;
                 padding: 1rem 1.2rem; margin: 1.2rem 0;
                 background: color-mix(in srgb, var(--bg) 95%, var(--fg) 5%); }}
article.sample > header {{ display: flex; align-items: center; gap: 0.7rem; margin-bottom: 0.6rem; }}
.duration {{ color: var(--muted); font-variant-numeric: tabular-nums; }}
.audio-id {{ color: var(--muted); font-size: 0.85rem; }}
audio {{ width: 100%; margin: 0.4rem 0 0.8rem; }}
table.transcriptions, table.verdicts {{ width: 100%; border-collapse: collapse;
                                       margin: 0.3rem 0; font-size: 0.95rem; }}
table.transcriptions th, table.transcriptions td,
table.verdicts th, table.verdicts td {{ border: 1px solid var(--border);
                                       padding: 0.5rem 0.7rem; vertical-align: top; text-align: left; }}
table.transcriptions th {{ width: 220px; white-space: nowrap; font-weight: 500;
                          background: color-mix(in srgb, var(--bg) 80%, var(--fg) 20%); }}
table.transcriptions tr.row-ref td {{ background: var(--row-ref-bg); }}
table.transcriptions tr.row-regime td {{ background: var(--row-regime-bg); }}
.empty {{ color: var(--muted); font-style: italic; }}
details {{ margin: 0.6rem 0; }}
details summary {{ cursor: pointer; color: var(--accent); padding: 0.3rem 0; font-weight: 500; }}
table.verdicts th {{ background: color-mix(in srgb, var(--bg) 80%, var(--fg) 20%);
                    font-weight: 500; font-size: 0.85rem; }}
table.verdicts td {{ font-variant-numeric: tabular-nums; text-align: center; }}
table.verdicts td.verdict-prose {{ text-align: left; font-size: 0.9rem; color: var(--muted); }}
.note-label {{ display: block; margin-top: 0.8rem; margin-bottom: 0.25rem;
              font-size: 0.85rem; color: var(--muted); }}
textarea.user-note {{ width: 100%; font-family: inherit; font-size: 0.95rem;
                     padding: 0.5rem 0.7rem; border: 1px solid var(--border); border-radius: 4px;
                     background: var(--bg); color: var(--fg); resize: vertical; }}
textarea.user-note:focus {{ outline: 2px solid var(--accent); outline-offset: 1px; }}
.toolbar {{ position: fixed; bottom: 1rem; right: 1rem; display: flex; gap: 0.5rem;
           background: var(--bg); padding: 0.5rem; border: 1px solid var(--border);
           border-radius: 8px; box-shadow: 0 2px 12px rgba(0,0,0,0.15); }}
.toolbar button {{ font-family: inherit; font-size: 0.85rem; padding: 0.4rem 0.8rem;
                  border: 1px solid var(--border); border-radius: 4px;
                  background: var(--bg); color: var(--fg); cursor: pointer; }}
.toolbar button:hover {{ border-color: var(--accent); color: var(--accent); }}
.toolbar .status {{ color: var(--muted); font-size: 0.8rem; align-self: center; }}
</style>
</head>
<body>
<header class="page">
  <h1>Comparaison qualitative — {html.escape(run_label)}</h1>
  <p class="meta">{total} samples · références et régimes auto-découverts depuis le corpus et les résultats</p>
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
  const STORAGE_KEY = 'deckle-bench-notes/{html.escape(run_label)}';
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
  saveAll(loadAll());
  document.getElementById('export-notes').addEventListener('click', () => {{
    const data = loadAll();
    const blob = new Blob([JSON.stringify(data, null, 2)], {{type: 'application/json'}});
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `${{STORAGE_KEY.replace(/[\\\\/:]/g, '_')}}.json`;
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


if __name__ == "__main__":
    main()
