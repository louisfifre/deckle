"""Bench voxtral-validation — Voxtral 24B Q4_K_M vs ground-truth Gemini.

Pour chaque (sample, régime) du corpus ``voxtral-val-30`` :
  1. Transcrit via ``voxtral-llamacpp`` (stack Vulkan validée).
  2. Calcule WER + CER **contre la référence Gemini** (champ
     ``payload.reference_text_gemini``), pas contre la référence Whisper
     qui hallucine sur les samples bruités ou très courts.
  3. Calcule ``word_count_ratio = words(hyp) / words(ref_gemini)`` —
     un proxy lisible pour détecter le lissage (ratio < 0.7) versus la
     fidélité verbatim (ratio 0.9–1.1).
  4. Calcule looping + leak patterns (mêmes métriques que voxtral-poc).
  5. Score via le juge Gemini multimodal (audio + hyp) — déjà câblé,
     écoute le signal et produit fidelite_signal / proprete /
     absence_hallucination / regime_respecte.
  6. Écrit une row JSON par expérience dans
     ``runs/voxtral-validation-<id>/results.jsonl``.

Différences avec ``voxtral-poc/bench.py`` :
  - source unique ``voxtral-llamacpp`` (pas de switcher DirectML)
  - judge unique Gemini (pas d'option Claude)
  - référence WER = Gemini (pas Whisper)
  - métrique supplémentaire ``word_count_ratio``
  - pas de monitor PowerShell GPU (overkill pour valider la qualité ;
    la perf a déjà été mesurée en session perf-cap 2026-05-26)
  - TOML régimes lit ``prompt`` + ``system_prompt`` distincts (la
    source supporte ``--system-prompt`` Mistral V7)

Préalable :
  - Avoir construit le corpus ``voxtral-val-30`` via
    ``python benchmark/asr/build_corpus.py --corpus voxtral-val-30``.
  - Avoir un champ ``reference_text_gemini`` déjà renseigné dans le corpus
    pour les samples à juger.
  - ``GEMINI_API_KEY`` dans ``benchmark/.env``.

Usage typique :
    python bench.py                              # 30 samples × 6 régimes
    python bench.py --regimes T1_baseline,T2_verbatim
    python bench.py --limit 5                    # 5 premiers samples
    python bench.py --skip-judge                 # metrics objectives seulement

Référence : brief session 2026-05-26.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
import time
import tomllib
from dataclasses import asdict
from datetime import datetime
from pathlib import Path

BENCH_DIR      = Path(__file__).resolve().parent
BENCHMARK_ROOT = BENCH_DIR.parent.parent
BENCHMARK_DIR  = BENCHMARK_ROOT.parent
sys.path.insert(0, str(BENCHMARK_DIR))

from lib._base_compat import _ensure_stdout_utf8
from asr.lib import corpus
from lib import paths
from lib.env import load_dotenv
from lib.event_log import EventLog
from asr.lib.metrics import wer    as metric_wer
from asr.lib.metrics import looping as metric_looping
from asr.lib.metrics import leak    as metric_leak


DEFAULT_CORPUS_SLUG = "voxtral-val-30"
REGIMES_PATH        = BENCHMARK_ROOT / "prompts" / "transcription" / "voxtral_validation.toml"
JUDGE_PROMPT_PATH   = BENCHMARK_ROOT / "prompts" / "judges" / "gemini_per_row.md"


def main() -> int:
    _ensure_stdout_utf8()
    args = _parse_args()
    t0 = time.perf_counter()

    # ── Setup ──────────────────────────────────────────────────────────
    load_dotenv(BENCHMARK_DIR / ".env")

    samples = corpus.load(args.corpus)
    if args.limit > 0:
        samples = samples[:args.limit]
    if not samples:
        print(f"FATAL : aucun sample dans corpus {args.corpus!r}", file=sys.stderr)
        return 2

    # Filtre samples sans référence Gemini — on imprime un warning mais
    # on continue avec ceux qui en ont. La pré-gen est censée tourner
    # avant ; un corpus sans Gemini = exécution bench inutile.
    n_total = len(samples)
    samples = [s for s in samples if s.reference_text_gemini.strip()]
    n_skipped = n_total - len(samples)
    if n_skipped:
        print(f"⚠ {n_skipped}/{n_total} samples sans reference_text_gemini — skipped.",
              file=sys.stderr)
    if not samples:
        print(f"FATAL : tous les samples sont sans reference_text_gemini.\n"
              f"  Renseigner reference_text_gemini dans le corpus {args.corpus!r} "
              f"avant de lancer ce bench figé.", file=sys.stderr)
        return 2

    regimes = _load_regimes(args.regimes)

    print(f"=== bench voxtral-validation ===")
    print(f"  corpus  : {args.corpus} ({len(samples)} samples utilisables)")
    print(f"  régimes : {list(regimes.keys())}")
    print(f"  judge   : {'skipped' if args.skip_judge else 'gemini (multimodal)'}")

    # ── Source ─────────────────────────────────────────────────────────
    if args.source == "voxtral-transformers":
        from asr.lib.sources.voxtral_transformers import VoxtralTransformersSource
        source = VoxtralTransformersSource()
    elif args.source == "voxtral-llamacpp":
        from asr.lib.sources.voxtral_llamacpp import VoxtralLlamacppSource
        source = VoxtralLlamacppSource()
    else:
        print(f"FATAL : source inconnue {args.source!r}. Dispo : "
              f"voxtral-llamacpp, voxtral-transformers", file=sys.stderr)
        return 2
    print(f"  source ready : {source.label}")

    # ── Judge ──────────────────────────────────────────────────────────
    judge = None
    if not args.skip_judge:
        judge = _build_judge()
        if judge:
            print(f"  judge ready  : {judge.label} model={judge.row_model}")

    # ── Run dir ────────────────────────────────────────────────────────
    # Nommage canonique `<modèle>-<phase>-<NNNN>` (cf. lib/paths.py). Run
    # dir vit sous %LOCALAPPDATA%\Deckle\benchmark\runs\ pour survivre aux
    # worktrees. Override via --run-name si besoin d'un nom custom.
    if args.run_name:
        run_dir = paths.RUNS_DIR / args.run_name
        run_dir.mkdir(parents=True, exist_ok=True)
    else:
        run_dir = paths.make_run_dir(model=args.source, phase="testing")
    results_path = run_dir / "results.jsonl"
    events_path  = run_dir / "events.jsonl"
    print(f"  run_dir : {run_dir}\n")

    # ── Event log ──────────────────────────────────────────────────────
    log = EventLog(events_path)
    log.event("bench_start", corpus=args.corpus,
              source=source.name, judge="gemini" if judge else "none",
              regimes=list(regimes.keys()), n_samples=len(samples))

    # ── Loop ───────────────────────────────────────────────────────────
    total = 0
    fail  = 0
    with results_path.open("w", encoding="utf-8") as fout:
        for si, sample in enumerate(samples, 1):
            for regime_name, regime_cfg in regimes.items():
                tag = f"[{si}/{len(samples)} {regime_name}]"
                user_prompt   = regime_cfg.get("prompt", "")
                system_prompt = regime_cfg.get("system_prompt", "") or None

                # Filter very-short × chat-mode : Voxtral en chat dégénère
                # sur les samples silencieux (réponses templates identiques
                # cross-samples observées 2026-05-27 sur T4/T5, dérive 128
                # tokens sur T6). Le mode canonique (prompt ET system_prompt
                # vides) traverse sans risque — il reste robuste sur ces
                # samples. La détection chat reproduit l'heuristique de la
                # source voxtral-transformers.
                is_chat = bool(user_prompt) or bool(system_prompt)
                if is_chat and sample.tier == "very-short":
                    print(f"{tag} {sample.id[:8]}… ({sample.duration_s:>5.1f}s) "
                          f"SKIP (very-short × chat)", flush=True)
                    log.event("row_skipped", sample_id=sample.id,
                              regime=regime_name, tier=sample.tier,
                              reason="very-short × chat-mode")
                    continue

                print(f"{tag} {sample.id[:8]}… ({sample.duration_s:>5.1f}s)",
                      end=" ", flush=True)

                log.event("row_start", sample_id=sample.id, regime=regime_name,
                          audio_s=sample.duration_s, tier=sample.tier)
                t_row = time.perf_counter()
                trans = source.transcribe(
                    audio_path    = sample.audio_path,
                    prompt        = user_prompt,
                    system_prompt = system_prompt,
                    on_event      = log.event,
                )

                row = _build_row(
                    sample       = sample,
                    source_name  = source.name,
                    source_label = source.label,
                    regime_name  = regime_name,
                    regime_cfg   = regime_cfg,
                    trans        = trans,
                )
                row["metrics"] = _compute_metrics(
                    hypothesis = trans.text,
                    reference  = sample.reference_text_gemini,
                )
                row["judge"] = None
                if judge is not None and trans.ok and trans.text:
                    try:
                        score = judge.score_row(
                            hypothesis   = trans.text,
                            reference    = sample.reference_text_gemini,
                            regime_name  = regime_name,
                            regime_label = regime_cfg.get("label", regime_name),
                            source_name  = source.name,
                            audio_path   = sample.audio_path,
                        )
                        row["judge"] = asdict(score)
                    except Exception as e:
                        row["judge"] = {"error": f"{type(e).__name__}: {e}"}

                fout.write(json.dumps(row, ensure_ascii=False) + "\n")
                fout.flush()
                total += 1
                if not trans.ok:
                    fail += 1
                dt_row = time.perf_counter() - t_row
                _print_summary(row, dt=dt_row)
                log.event("row_end", sample_id=sample.id, regime=regime_name,
                          ok=trans.ok, elapsed_s=dt_row,
                          wer=row["metrics"].get("wer"))

    elapsed = time.perf_counter() - t0
    log.event("bench_end", total_rows=total, fail=fail, elapsed_s=elapsed)
    log.close()

    print()
    print(f"✓ done — {total} rows ({fail} fail) en {elapsed:.1f}s")
    print(f"  results : {results_path}")
    print(f"  events  : {events_path}")
    return 0


# ── Sub-builders ────────────────────────────────────────────────────────

def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--corpus",   default=DEFAULT_CORPUS_SLUG,
                   help=f"Slug du corpus sous corpora/<slug>/ (défaut : {DEFAULT_CORPUS_SLUG}).")
    p.add_argument("--source",   default="voxtral-llamacpp",
                   choices=["voxtral-llamacpp", "voxtral-transformers"],
                   help="Backend de transcription Voxtral à utiliser.")
    p.add_argument("--regimes",  default="all",
                   help="Liste virgulée (ex. T1_baseline,T2_verbatim) ou 'all'.")
    p.add_argument("--limit",    type=int, default=0,
                   help="N premiers samples du corpus (0 = tous).")
    p.add_argument("--skip-judge", action="store_true",
                   help="Skip le judge Gemini. Métriques objectives seulement.")
    p.add_argument("--run-name", default="",
                   help="Nom custom du run sous RUNS_DIR (défaut : <source>-testing-NNNN).")
    return p.parse_args()


def _load_regimes(only: str) -> dict[str, dict]:
    with REGIMES_PATH.open("rb") as f:
        all_regimes = tomllib.load(f)
    if only == "all":
        return all_regimes
    wanted = {c.strip() for c in only.split(",") if c.strip()}
    missing = wanted - all_regimes.keys()
    if missing:
        print(f"FATAL : régimes inconnus {missing}. Dispo dans "
              f"{REGIMES_PATH.name} : {list(all_regimes.keys())}",
              file=sys.stderr)
        sys.exit(1)
    return {k: v for k, v in all_regimes.items() if k in wanted}


def _build_judge():
    """Instancie le juge Gemini multimodal. Retourne None si la clé
    n'est pas dispo (warning loud, bench continue sans judge)."""
    import os
    if not os.environ.get("GEMINI_API_KEY"):
        print("  ⚠ GEMINI_API_KEY absente — judge skipped.\n"
              "    Créer benchmark/.env avec : GEMINI_API_KEY=AIza...",
              file=sys.stderr)
        return None
    if not JUDGE_PROMPT_PATH.exists():
        print(f"  ⚠ prompt judge absent : {JUDGE_PROMPT_PATH.name} — judge skipped.",
              file=sys.stderr)
        return None
    from asr.lib.judges.gemini import GeminiJudge
    return GeminiJudge(row_system_prompt=JUDGE_PROMPT_PATH.read_text(encoding="utf-8"))


def _build_row(*, sample, source_name, source_label, regime_name,
               regime_cfg, trans) -> dict:
    return {
        "audio_id":               sample.id,
        "audio_file":             sample.audio_path.name,
        "audio_seconds":          sample.duration_s,
        "tier":                   sample.tier,
        "reference_text_whisper": sample.reference_text,
        "reference_text_gemini":  sample.reference_text_gemini,
        "reference_words_whisper": sample.reference_words,
        "source":                 source_name,
        "source_label":           source_label,
        "regime":                 regime_name,
        "regime_label":           regime_cfg.get("label", regime_name),
        "regime_user_prompt":     regime_cfg.get("prompt", ""),
        "regime_system_prompt":   regime_cfg.get("system_prompt", ""),
        "ok":                     trans.ok,
        "error":                  trans.error,
        "text":                   trans.text,
        "elapsed_s":              trans.elapsed_s,
        "rtf":                    trans.rtf,
        "generated_tokens":       trans.generated_tokens,
        "extras":                 trans.extras,
        "timestamp":              datetime.now().isoformat(timespec="seconds"),
    }


def _compute_metrics(*, hypothesis: str, reference: str) -> dict:
    """Métriques objectives. Référence = Gemini ground-truth.

    ``word_count_ratio`` = words(hyp) / words(ref) — proxy lisible pour
    différencier le lissage (ratio < 0.7) de la fidélité verbatim
    (ratio 0.9–1.1). N'est PAS une mesure de qualité en soi : un ratio
    parfait ne dit rien sur l'ordre des mots ou les substitutions. À
    lire conjointement avec WER.
    """
    er = metric_wer.compute(reference, hypothesis)
    lo = metric_looping.compute(hypothesis)
    lk = metric_leak.detect(hypothesis)
    hyp_words = len(hypothesis.split())
    ref_words = len(reference.split())
    return {
        "wer":               None if math.isnan(er.wer) else er.wer,
        "cer":               None if math.isnan(er.cer) else er.cer,
        "word_count_ratio":  hyp_words / ref_words if ref_words > 0 else None,
        "looping_score":     lo.score,
        "longest_ngram":     list(lo.longest_ngram),
        "hallucination_hits": lk.hallucinations,
        "custom_leak_hits":  lk.custom_leaks,
        "char_count":        len(hypothesis),
        "word_count":        hyp_words,
        "ref_word_count":    ref_words,
    }


def _print_summary(row: dict, *, dt: float) -> None:
    if not row["ok"]:
        print(f"FAIL {row['error'][:80]}")
        return
    m = row.get("metrics") or {}
    wer = m.get("wer")
    wer_s = "n/a" if wer is None else f"{wer:.2f}"
    ratio = m.get("word_count_ratio")
    ratio_s = "n/a" if ratio is None else f"{ratio:.2f}"
    judge = row.get("judge") or {}
    axes = judge.get("axes") or {} if isinstance(judge, dict) else {}
    fid  = axes.get("fidelite_signal", "-")
    pro  = axes.get("proprete", "-")
    halu = axes.get("absence_hallucination", "-")
    reg  = axes.get("regime_respecte", "-")
    parts = [
        f"RTF {row['rtf']:.2f}",
        f"wer {wer_s}",
        f"ratio {ratio_s}",
    ]
    if axes:
        parts.append(f"j {fid}/{pro}/{halu}/{reg}")
    parts.append(f"[{dt:.1f}s]")
    print(" ".join(parts))


if __name__ == "__main__":
    sys.exit(main())
