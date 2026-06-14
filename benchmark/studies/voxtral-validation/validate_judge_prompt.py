"""Validation du prompt judge corrigé — challenger Gemini avant grande échelle.

Avant d'investir dans des évaluations de centaines d'extraits qui s'appuient
sur le judge Gemini, on veut s'assurer que la correction du prompt
(commit fix(bench) du 2026-05-27 suite 3) produit bien les comportements
attendus sur la rubrique T1-T6 actualisée. Stratégie minimale :

  1. Cherry-pick depuis le run Q8_0 le plus récent (180 rows dispo) un
     échantillon ciblé qui couvre chaque régime, avec des cas variés
     (sortie propre, sortie dégénérée chat-mode, sortie correcte pour le
     régime non-transcription T5).
  2. Re-scorer chaque row avec le judge instancié sur le prompt corrigé.
  3. Imprimer côte-à-côte ancien score (lu depuis le results.jsonl) vs
     nouveau score, axe par axe, avec verdicts. Diagnostic en clair.

Sortie : un tableau lisible sur stdout. Pas de JSONL persisté — c'est un
test ponctuel de la rubrique, pas un run de référence.

Script ad-hoc, archivable une fois la confiance dans le judge établie.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

BENCH_DIR      = Path(__file__).resolve().parent
BENCHMARK_ROOT = BENCH_DIR.parent.parent
sys.path.insert(0, str(BENCHMARK_ROOT))

from lib._base_compat import _ensure_stdout_utf8
from lib.env import load_dotenv

LOCALAPPDATA = Path(os.environ["LOCALAPPDATA"])
RUNS_DIR     = LOCALAPPDATA / "Deckle" / "benchmark" / "runs"
CORPORA_DIR  = LOCALAPPDATA / "Deckle" / "benchmark" / "corpora"

Q8_RUN       = RUNS_DIR / "voxtral-llamacpp-mini3b-q8-validation-0001" / "results.jsonl"
JUDGE_PROMPT = BENCHMARK_ROOT / "prompts" / "judges" / "gemini_per_row.md"


# Cherry-pick — couvre chaque régime, mix propre/dégénéré.
# Format : (audio_id_prefix, regime, intention_du_test)
CHERRY_PICKS = [
    # ── T1_baseline : un propre + un dégénéré chat-mode
    ("a0ae729a", "T1_baseline",   "T1 propre (4.2s) — sortie de transcription correcte"),
    ("dcad692a", "T1_baseline",   "T1 dégénéré (1.7s) — Q8 part en dissertation chat-mode"),

    # ── T2_verbatim : un propre + un dégénéré
    ("79f66a7c", "T2_verbatim",   "T2 propre (5.0s) — verbatim correct"),
    ("b9d726f4", "T2_verbatim",   "T2 dégénéré (1.1s) — chat-mode pur"),

    # ── T3_translate : un propre EN + un (à voir)
    ("a0ae729a", "T3_translate",  "T3 propre — sortie EN attendue, fidélité sémantique"),
    ("9b8c6405", "T3_translate",  "T3 sample long — sortie EN sur audio FR 10s"),

    # ── T4_summary : un propre + un long
    ("a0ae729a", "T4_summary",    "T4 propre — résumé une phrase"),
    ("bc08abb2", "T4_summary",    "T4 sample 13s — résumé d'un contenu plus dense"),

    # ── T5_qa_register : convention nouvelle fidelite_signal=100
    ("a0ae729a", "T5_qa_register", "T5 régime non-transcription — fidelite_signal attendu à 100"),
    ("79f66a7c", "T5_qa_register", "T5 autre sample — vérif convention stable"),

    # ── T6_sys_prompt : transcription + étiquette ton
    ("a0ae729a", "T6_sys_prompt", "T6 propre — transcription + étiquette ton"),
    ("8ebfbfa2", "T6_sys_prompt", "T6 sample 5s — étiquette ton attendue"),
]


def load_rows() -> list[dict]:
    with Q8_RUN.open("r", encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def find_row(rows: list[dict], audio_prefix: str, regime: str) -> dict | None:
    for r in rows:
        if r.get("audio_id", "").startswith(audio_prefix) and r.get("regime") == regime:
            return r
    return None


def fmt_axes(axes: dict) -> str:
    if not axes:
        return "      —      "
    return (f"{axes.get('fidelite_signal', '?'):>3}/"
            f"{axes.get('proprete', '?'):>3}/"
            f"{axes.get('absence_hallucination', '?'):>3}/"
            f"{axes.get('regime_respecte', '?'):>3}")


def short(s: str, n: int) -> str:
    s = (s or "").replace("\n", " ").strip()
    return s if len(s) <= n else s[:n - 1] + "…"


def main() -> int:
    _ensure_stdout_utf8()
    load_dotenv()

    if not os.environ.get("GEMINI_API_KEY"):
        print("FATAL : GEMINI_API_KEY non défini dans benchmark/.env", file=sys.stderr)
        return 2

    print(f"=== Validation du prompt judge corrigé ===")
    print(f"  prompt : {JUDGE_PROMPT}")
    print(f"  source : {Q8_RUN}")
    print(f"  picks  : {len(CHERRY_PICKS)} rows ciblés")
    print()

    rows = load_rows()
    system_prompt = JUDGE_PROMPT.read_text(encoding="utf-8")

    from lib.judges.gemini import GeminiJudge
    judge = GeminiJudge(row_system_prompt=system_prompt)
    print(f"  judge ready  : {judge.label} model={judge.row_model}")
    print()

    audio_dir = CORPORA_DIR / "voxtral-val-30"

    header = (f"{'#':>2}  {'sample':<10} {'régime':<16} "
              f"{'old f/p/h/r':<16} {'new f/p/h/r':<16} ΔΔ")
    print(header)
    print("─" * len(header))

    deltas: list[tuple[str, str, dict, dict, str, str, str]] = []
    for i, (prefix, regime, why) in enumerate(CHERRY_PICKS, 1):
        row = find_row(rows, prefix, regime)
        if not row:
            print(f"{i:>2}. {prefix:<10} {regime:<16} ROW INTROUVABLE")
            continue

        audio_path = audio_dir / row["audio_file"]
        old_axes   = (row.get("judge") or {}).get("axes") or {}
        old_verdict = (row.get("judge") or {}).get("verdict") or ""

        try:
            new_score = judge.score_row(
                hypothesis   = row["text"],
                reference    = row.get("reference_text_gemini"),
                regime_name  = regime,
                regime_label = row.get("regime_label", regime),
                source_name  = row.get("source", "voxtral-llamacpp"),
                audio_path   = audio_path,
            )
        except Exception as exc:
            print(f"{i:>2}. {prefix:<10} {regime:<16} JUDGE ERROR : {type(exc).__name__}: {exc}")
            continue

        new_axes = new_score.axes or {}
        new_verdict = new_score.verdict or ""

        # Delta perceptible : axes qui ont bougé
        deltas_str = ""
        for axis in ("fidelite_signal", "proprete", "absence_hallucination", "regime_respecte"):
            o = old_axes.get(axis)
            n = new_axes.get(axis)
            if o is not None and n is not None and o != n:
                short_name = {"fidelite_signal": "f", "proprete": "p",
                              "absence_hallucination": "h", "regime_respecte": "r"}[axis]
                sign = "+" if n > o else ""
                deltas_str += f" {short_name}:{sign}{n - o}"

        print(f"{i:>2}. {prefix:<10} {regime:<16} "
              f"{fmt_axes(old_axes):<16} {fmt_axes(new_axes):<16}{deltas_str}")
        deltas.append((prefix, regime, old_axes, new_axes, old_verdict, new_verdict, why))

    # ── Verdicts détaillés ─────────────────────────────────────────────
    print()
    print("=== Verdicts détaillés ===")
    for prefix, regime, old_axes, new_axes, old_verdict, new_verdict, why in deltas:
        print(f"\n[{prefix} / {regime}] — {why}")
        print(f"  old axes : {fmt_axes(old_axes)}   verdict : {short(old_verdict, 200)}")
        print(f"  new axes : {fmt_axes(new_axes)}   verdict : {short(new_verdict, 200)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
