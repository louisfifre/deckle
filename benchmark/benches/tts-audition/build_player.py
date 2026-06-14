"""Build the listening page (ecouter.html) for the ONNX-local French audition.

Harness-driven: scans `_harness.RUN_DIR` for `onnx_*.wav`, groups by engine and
voice, reads the sentence texts at RUNTIME from `_harness` (public + private
corpus — never inlined here), and renders an auto-generated STATS panel from the
`_stats.jsonl` each synth run appends to. The panel's timestamp is the at-a-glance
"is this fresh?" signal. The rendered HTML lands in the gitignored run dir.
Re-runnable after any partial batch.
"""

from __future__ import annotations

import html
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import _harness  # noqa: E402

OUT = _harness.RUN_DIR

# Texts read at runtime: public + expressive tags + private corpus.
_CORPUS = _harness.corpus_sentences()
TEXTS = {**_harness.PUBLIC_SENTENCES, **_harness.EXPRESSIVE_TAGS, **_CORPUS}
CORPUS_IDS = list(_CORPUS.keys())

ID_ORDER = (["01_neutre", "02_explication", "03_emotion",
             "03_emotion_calme", "03_emotion_intense", "04_tics", "05_question"]
            + CORPUS_IDS + ["tags_rire", "tags_emotion"])

ID_LABEL = {
    "01_neutre": "Neutre", "02_explication": "Explication", "03_emotion": "Émotion",
    "03_emotion_calme": "Émotion — calme (exag. 0.3)",
    "03_emotion_intense": "Émotion — intense (exag. 0.8)",
    "04_tics": "Tics (texte)", "05_question": "Question",
    "tags_rire": "Tags — rire/soupir", "tags_emotion": "Tags — émotion/souffle",
}


def id_label(sid: str) -> str:
    if sid in ID_LABEL:
        return ID_LABEL[sid]
    if sid.startswith("corpus_"):
        return "Corpus " + sid.split("_", 1)[1]
    return sid


# engine prefix -> (display label, honest note). Judgement-by-ear is Louis's.
ENGINES = [
    ("chatterbox", "Chatterbox-ML", "Clonage zéro-shot : le timbre ET l'accent viennent de la référence. "
                                    "La voix par défaut est anglaise — d'où l'accent anglo. Les variantes « réf. FR » testent "
                                    "une référence française ; « voix plate » baisse température + exagération. Licence MIT — commercial libre."),
    ("supertonic", "Supertonic-3", "Expressif, voix fixes (pas de clonage). Aucun canal de tags : les &lt;laugh&gt; seraient lus tels quels — réservés à Orpheus."),
    ("orpheus", "Orpheus français", "3B expressif, le seul à faire les vrais tags &lt;laugh&gt;/&lt;sigh&gt; arbitraires. Lourd : GPU nécessaire."),
    ("f5", "F5-TTS français", "Clonage zéro-shot — emprunte le timbre d'une référence. Licence CC-BY-NC : usage perso seulement."),
    ("piper", "Piper (VITS)", "Propre et intelligible, mais plat — aucune émotion apprise. Base de référence."),
]

VOICE_LABEL = {
    "supertonic_M1": "M1 (masculine)",
    "chatterbox": "réf. anglaise par défaut (l'accent anglo)",
    "chatterbox_frSupertonic": "réf. FR — Supertonic M1 (H)",
    "chatterbox_frPierre": "réf. FR — Piper Pierre (H)",
    "chatterbox_frJessica": "réf. FR — Piper Jessica (F)",
    "chatterbox_flatPierre": "réf. FR Pierre — voix plate (temp 0.5 / exag 0.3)",
    "f5_fr": "clone — réf. (fr)",
    "orpheus": "voix CML-FR",
    "piper_upmc_s0": "upmc · jessica (féminine)",
    "piper_upmc_s1": "upmc · pierre (masculine)",
}

# Static model facts (from the inspection) -> (licence, poids, params).
MODEL_FACTS = {
    "chatterbox": ("MIT — commercial libre", "~2,2 Go", "~0,5 Md"),
    "supertonic": ("OpenRAIL-M — conditionnel", "~0,4 Go", "~99 M"),
    "orpheus": ("Apache-2.0 (gated)", "~13,3 Go", "~3,3 Md"),
    "f5": ("CC-BY-NC — perso seulement", "~1,45 Go", "~336 M"),
    "piper": ("MIT + voix (siwis CC-BY-4.0)", "~77 Mo", "~15 M/voix"),
}


def facts_for(model: str):
    for k, v in MODEL_FACTS.items():
        if model.startswith(k):
            return v
    return ("—", "—", "—")


def parse(stem: str):
    """onnx_<engine>_<voice...>_<id> -> (engine, engine_voice, id). None if no id match."""
    rest = stem[len("onnx_"):]
    for sid in ID_ORDER:
        if rest.endswith("_" + sid):
            ev = rest[: -(len(sid) + 1)]
            return ev.split("_", 1)[0], ev, sid
    return None


def player(name: str, label: str) -> str:
    return (f'<div class="take"><div class="take-label">{html.escape(label)}</div>'
            f'<audio controls preload="none" src="{html.escape(name)}"></audio></div>')


def load_stats():
    """Latest run record per (model, voice) from _stats.jsonl, + the newest ts."""
    p = OUT / "_stats.jsonl"
    if not p.exists():
        return {}, None
    latest: dict = {}
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        r = json.loads(line)
        key = (r["model"], r.get("voice", ""))
        if key not in latest or r["ts"] > latest[key]["ts"]:
            latest[key] = r
    newest = max((r["ts"] for r in latest.values()), default=None)
    return latest, newest


def stats_panel() -> str:
    stats, newest = load_stats()
    if not stats:
        return ""
    rows = []
    for (model, voice), r in sorted(stats.items()):
        lic, size, params = facts_for(model)
        n = r.get("n")
        comp = r.get("compute_s")
        per = f"{comp / n:.1f}s" if (comp and n) else "—"
        rtf = r.get("rtf")
        rtf_s = f"{rtf}×" if rtf else "—"
        name = f"{model} · {voice}" if voice else model
        when = r["ts"][:16].replace("T", " ")
        rows.append(
            f"<tr><td><b>{html.escape(name)}</b></td><td>{html.escape(lic)}</td>"
            f"<td>{params}</td><td>{size}</td><td>{r.get('ep', '—')}</td>"
            f"<td>{n or '—'}</td><td>{per}</td><td>{rtf_s}</td><td>{when}</td></tr>")
    head = newest[:16].replace("T", " ") if newest else "—"
    return ('<section class="card"><h2>Stats — dernier run ' + html.escape(head) + '</h2>'
            '<table class="stats"><thead><tr><th>Modèle</th><th>Licence</th><th>Params</th>'
            '<th>Poids</th><th>EP</th><th>Phr.</th><th>Compute/phr.</th><th>RTF</th><th>Généré</th></tr></thead>'
            f'<tbody>{"".join(rows)}</tbody></table>'
            '<p class="note">RTF = durée audio / temps de calcul (&gt;1× = plus rapide que le temps réel), mesuré sur CPU. '
            'La colonne « Généré » dit si c\'est frais.</p></section>')


def main() -> int:
    items = {}  # engine_voice -> {id: filename}
    engine_of = {}
    for p in sorted(OUT.glob("onnx_*.wav")):
        parsed = parse(p.stem)
        if not parsed:
            continue
        engine, ev, sid = parsed
        items.setdefault(ev, {})[sid] = p.name
        engine_of[ev] = engine

    # ── Highlight: expressive tag takes from whichever engine has them ────────
    highlight = []
    for ev, byid in items.items():
        for sid in ("tags_rire", "tags_emotion"):
            if sid in byid:
                highlight.append(player(byid[sid], f"{VOICE_LABEL.get(ev, ev)} — {id_label(sid)}"))
    highlight_html = ""
    if highlight:
        ex_texts = "".join(f'<p class="text">« {html.escape(TEXTS[s])} »</p>' for s in ("tags_rire", "tags_emotion"))
        highlight_html = ('<section class="card hot"><h2>Tags expressifs — le test « feel ChatGPT »</h2>'
                          + ex_texts + f'<div class="takes">{"".join(highlight)}</div></section>')

    # ── Per engine, per voice ─────────────────────────────────────────────────
    sections = []
    for engine, elabel, enote in ENGINES:
        evs = [ev for ev in items if engine_of[ev] == engine]
        if not evs:
            continue
        cards = []
        for ev in sorted(evs):
            takes = []
            for sid in ID_ORDER:
                if sid in items[ev]:
                    takes.append(player(items[ev][sid], id_label(sid)))
            cards.append(f'<div class="voice"><h3>{html.escape(VOICE_LABEL.get(ev, ev))}</h3>'
                         f'<div class="takes">{"".join(takes)}</div></div>')
        sections.append(f'<section class="card"><h2>{html.escape(elabel)}</h2>'
                        f'<p class="note">{enote}</p>{"".join(cards)}</section>')

    sentences_ref = "".join(
        f'<li><b>{html.escape(id_label(s))}</b> — « {html.escape(TEXTS[s])} »</li>'
        for s in ID_ORDER if s in TEXTS)

    doc = f"""<!doctype html>
<html lang="fr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Audition TTS ONNX-local — Deckle</title>
<style>
  :root {{ color-scheme: light dark;
    --bg:#fafafa; --card:#fff; --ink:#1a1a1a; --mut:#666; --line:#e6e6e6; --accent:#3b6ea5; --hot:#b4690e; }}
  @media (prefers-color-scheme: dark) {{ :root {{
    --bg:#1b1b1f; --card:#26262b; --ink:#f0f0f2; --mut:#a0a0a8; --line:#36363c; --accent:#6ea3dc; --hot:#e0a458; }} }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; padding:32px 20px 64px; background:var(--bg); color:var(--ink);
    font:15px/1.55 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif; }}
  .wrap {{ max-width:920px; margin:0 auto; }}
  h1 {{ font-size:24px; font-weight:650; margin:0 0 6px; }}
  .sub {{ color:var(--mut); margin:0 0 24px; }}
  .card {{ background:var(--card); border:1px solid var(--line); border-radius:12px; padding:18px 20px; margin:0 0 16px; }}
  .card.hot {{ border-color:var(--hot); }}
  h2 {{ font-size:13px; font-weight:600; text-transform:uppercase; letter-spacing:.04em; color:var(--accent); margin:0 0 8px; }}
  .card.hot h2 {{ color:var(--hot); }}
  h3 {{ font-size:14px; font-weight:600; margin:18px 0 8px; color:var(--ink); }}
  .note {{ color:var(--mut); font-size:13px; margin:0 0 8px; }}
  .text {{ font-size:15px; margin:0 0 10px; }}
  .takes {{ display:grid; grid-template-columns:1fr 1fr; gap:12px; }}
  @media (max-width:640px) {{ .takes {{ grid-template-columns:1fr; }} }}
  .take-label {{ font-size:12px; color:var(--mut); margin-bottom:5px; }}
  audio {{ width:100%; height:34px; }}
  table.stats {{ width:100%; border-collapse:collapse; font-size:13px; }}
  table.stats th, table.stats td {{ text-align:left; padding:6px 8px; border-bottom:1px solid var(--line); white-space:nowrap; }}
  table.stats th {{ color:var(--mut); font-weight:600; }}
  .guide {{ background:var(--card); border:1px solid var(--line); border-radius:12px; padding:18px 20px; margin-top:22px; }}
  .guide h2 {{ color:var(--ink); }} .guide ul {{ margin:0; padding-left:20px; }} .guide li {{ margin:5px 0; }}
  .foot {{ color:var(--mut); font-size:13px; margin-top:22px; }}
</style></head>
<body><div class="wrap">
  <h1>Audition TTS — ONNX local</h1>
  <p class="sub">Échantillons synthétisés <b>en local</b>, en <b>ONNX Runtime pur</b>,
  sans Transformers ni inférence en ligne. Phrases publiques + extraits réels de ton corpus.</p>
  {stats_panel()}
  {highlight_html}
  {"".join(sections)}
  <div class="guide"><h2>Les phrases</h2><ul>{sentences_ref}</ul></div>
  <p class="foot">Reconstruction propre, en série (un moteur à la fois). N'apparaissent que tes validés.
  Repère décisif : <b>Chatterbox</b> est le seul en licence MIT (commercial libre).</p>
</div></body></html>"""

    dest = OUT / "ecouter.html"
    OUT.mkdir(parents=True, exist_ok=True)
    dest.write_text(doc, encoding="utf-8")
    n = sum(len(v) for v in items.values())
    print(f"Wrote {dest}  ({len(items)} voices, {n} samples)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
