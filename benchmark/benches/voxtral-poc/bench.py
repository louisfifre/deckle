"""Bench voxtral-poc — valider que Voxtral DML peut remplacer Whisper.

Pour chaque (sample, source, régime) :
  1. transcrit via la source choisie
  2. calcule WER + CER (vs référence Whisper du corpus)
  3. calcule looping + hallucination patterns
  4. si --judge claude : appelle Claude Haiku per-row (axes : fidélité,
     propreté, hallucinations, respect régime, verdict, suspect ref)
  5. écrit une row JSON par expérience dans runs/<run-id>/results.jsonl

Pourquoi ce bench existe :
  - Voxtral via stack canonique Mistral (apply_transcription_request)
    produit des transcriptions fidèles. Validé en smoke Phase 3, mais
    pas encore quantifié sur l'ensemble du corpus × 5 régimes ni jugé
    de manière fiable (le juge Ollama de Phase 1/2 manquait d'audio et
    pénalisait les verbatims fidèles).
  - On veut comparer V1 brut vs V2 lissé vs V3 verbatim vs V4 annoté
    vs V5 anglais pour voir lequel pourrait remplacer Whisper + Ollama
    rewrite dans Deckle.

Usage typique :
    python bench.py                              # all 5 regimes, judge Claude
    python bench.py --regimes V1_raw,V3_fidele   # subset
    python bench.py --skip-judge                 # quick metrics only
    python bench.py --limit 3                    # 3 premiers samples par durée

Référence : ADR-0014 (POC évaluation Voxtral).
"""

from __future__ import annotations

import argparse
import atexit
import json
import math
import subprocess
import sys
import time
import tomllib
from dataclasses import asdict
from datetime import datetime
from pathlib import Path

# Ajoute benchmark/ au sys.path pour pouvoir importer lib.* sans bidouille
# d'install. Les benches restent des scripts standalone, pas un package.
BENCH_DIR = Path(__file__).resolve().parent
BENCHMARK_ROOT = BENCH_DIR.parent.parent
sys.path.insert(0, str(BENCHMARK_ROOT))

from lib._base_compat import _ensure_stdout_utf8
from lib import corpus
from lib.env import load_dotenv
from lib.event_log import EventLog
from lib.metrics import wer as metric_wer
from lib.metrics import looping as metric_looping
from lib.metrics import leak as metric_leak

MONITOR_SCRIPT = BENCHMARK_ROOT / "lib" / "monitor" / "gpu_monitor.ps1"


DEFAULT_CORPUS_SLUG = "voxtral-poc"
# Un prompt par juge sous prompts/judges/<judge>_per_row.md. Le nom du
# fichier est dérivé du nom du juge (claude → claude_per_row.md, etc.).
JUDGE_PROMPTS_DIR = BENCHMARK_ROOT / "prompts" / "judges"

# Mapping source → fichier de régimes par défaut. Chaque source a son
# propre fichier parce que les régimes valides dépendent du mode (un
# régime "traduit EN" n'a aucun effet en mode transcription canonique
# qui ignore le prompt).
REGIMES_BY_SOURCE: dict[str, Path] = {
    "voxtral-transcribe": BENCHMARK_ROOT / "prompts" / "transcription" / "voxtral_transcribe.toml",
    "voxtral-chat":       BENCHMARK_ROOT / "prompts" / "transcription" / "voxtral_chat.toml",
    "whisper-cpp":        BENCHMARK_ROOT / "prompts" / "transcription" / "voxtral_transcribe.toml",  # placeholder
}


def main() -> int:
    _ensure_stdout_utf8()
    args = _parse_args()
    t0 = time.perf_counter()

    # ── Setup ──────────────────────────────────────────────────────────
    load_dotenv()

    samples = corpus.load(args.corpus)
    if args.limit > 0:
        samples = samples[:args.limit]
    if not samples:
        print(f"FATAL : aucun sample trouvé pour corpus {args.corpus!r}", file=sys.stderr)
        return 2

    regimes_path = REGIMES_BY_SOURCE.get(args.source)
    if regimes_path is None:
        print(f"FATAL : source {args.source!r} sans fichier régimes mappé.", file=sys.stderr)
        return 2
    regimes = _load_regimes(args.regimes, regimes_path)

    print(f"=== bench voxtral-poc ===")
    print(f"  corpus  : {args.corpus} ({len(samples)} samples)")
    print(f"  source  : {args.source} (dtype={args.dtype}{', cpu' if args.cpu else ''})")
    print(f"  régimes : {list(regimes.keys())}")
    print(f"  judge   : {'skipped' if args.skip_judge else args.judge}"
          f"{f' ({args.row_model})' if (args.row_model and not args.skip_judge) else ''}")

    # ── Judge ──────────────────────────────────────────────────────────
    # Construit avant la source parce qu'il ne pèse rien (juste un client
    # HTTP) ; ça permet d'isoler le chargement du modèle ASR plus bas,
    # encadré par les events model_load_{start,end} pour la mesure VRAM.
    judge = None
    if not args.skip_judge:
        judge = _build_judge(args)
        if judge:
            print(f"  judge ready  : {judge.label} model={judge.row_model}")

    # ── Run dir ────────────────────────────────────────────────────────
    run_id = args.run_name or f"voxtral-poc-{datetime.now():%Y-%m-%d-%H%M}"
    run_dir = BENCHMARK_ROOT / "runs" / run_id
    run_dir.mkdir(parents=True, exist_ok=True)
    results_path = run_dir / "results.jsonl"
    events_path  = run_dir / "events.jsonl"
    monitor_path = run_dir / "monitor.jsonl"
    print(f"  run_dir : {run_dir}")

    # ── Monitor PowerShell auto ────────────────────────────────────────
    monitor_proc = _start_monitor(monitor_path) if not args.skip_monitor else None
    if monitor_proc:
        print(f"  monitor : pwsh PID {monitor_proc.pid} → {monitor_path.name}")
    print()

    # ── Event log ──────────────────────────────────────────────────────
    log = EventLog(events_path)
    log.event("bench_start", corpus=args.corpus, source=args.source,
              dtype=args.dtype, regimes=list(regimes.keys()),
              n_samples=len(samples), max_new_tokens=args.max_new_tokens)

    # ── Source (chargement modèle, instrumenté pour mesure VRAM) ───────
    # Pauses larges pour que le monitor PowerShell (Get-Counter à 500 ms,
    # ~1-2 s de latence au démarrage) capte des échantillons stables sur
    # chaque phase. Sans ces marges les peaks par phase étaient noyés
    # dans le bruit de transition (cf. mini-run : 1-3 samples par row,
    # peak idle inexistant). 10 s = ~18-20 samples par phase de pause,
    # largement suffisant pour un baseline propre.
    if monitor_proc:
        time.sleep(10.0)
    log.event("model_load_start", source=args.source, dtype=args.dtype)
    source = _build_source(args)
    log.event("model_load_end", source=args.source, source_label=source.label,
              n_params=getattr(source, "n_params", None))
    print(f"  source ready : {source.label}")
    # Settle post-load : laisse DirectML stabiliser sa VRAM après l'alloc
    # initiale, et donne au monitor une fenêtre propre pour mesurer la
    # baseline post-load (avant que la 1re row ne déclenche les buffers
    # de génération qui inflent les peaks).
    if monitor_proc:
        time.sleep(10.0)

    # ── Loop ───────────────────────────────────────────────────────────
    # Import lazy : cleanup_gpu n'a de sens que pour les sources Voxtral.
    from lib.sources._voxtral_common import cleanup_gpu

    total = 0
    fail = 0
    with results_path.open("w", encoding="utf-8") as fout:
        for si, sample in enumerate(samples, 1):
            for ri, (regime_name, regime_cfg) in enumerate(regimes.items(), 1):
                tag = f"[{si}/{len(samples)} {regime_name}]"
                prompt = regime_cfg["system_prompt"]
                print(f"{tag} {sample.id[:8]}… ({sample.duration_s:>5.1f}s)", end=" ", flush=True)
                log.event("row_start", sample_id=sample.id, regime=regime_name,
                          source=source.name, audio_s=sample.duration_s,
                          tier=sample.tier)
                t_row = time.perf_counter()
                trans = source.transcribe(
                    audio_path=sample.audio_path,
                    prompt=prompt,
                    max_new_tokens=(args.max_new_tokens or None),
                    on_event=log.event,
                )
                row = _build_row(
                    sample=sample,
                    source_name=source.name,
                    source_label=source.label,
                    regime_name=regime_name,
                    regime_cfg=regime_cfg,
                    trans=trans,
                )
                # Métriques objectives (toujours)
                row["metrics"] = _compute_metrics(
                    hypothesis=trans.text,
                    reference=sample.reference_text,
                )
                # Juge (si actif et trans OK).
                # audio_path passé systématiquement : les juges textuels
                # l'ignorent, les juges multimodaux (Gemini) l'écoutent.
                row["judge"] = None
                if judge is not None and trans.ok and trans.text:
                    try:
                        score = judge.score_row(
                            hypothesis=trans.text,
                            reference=sample.reference_text,
                            regime_name=regime_name,
                            regime_label=regime_cfg.get("label", regime_name),
                            source_name=source.name,
                            audio_path=sample.audio_path,
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
                          wer=row["metrics"].get("wer"),
                          new_tokens=trans.generated_tokens)
                # Mini-cleanup entre rows pour aider DirectML à libérer la
                # VRAM avant la suivante (pas d'OOM espéré, mais c'est cheap).
                if args.source.startswith("voxtral-"):
                    cleanup_gpu(sleep_s=args.inter_row_sleep, on_event=log.event)

    elapsed = time.perf_counter() - t0
    log.event("bench_end", total_rows=total, fail=fail, elapsed_s=elapsed)
    log.close()
    _stop_monitor(monitor_proc)

    print()
    print(f"✓ done — {total} rows ({fail} fail) en {elapsed:.1f}s")
    print(f"  results : {results_path}")
    print(f"  events  : {events_path}")
    if monitor_proc:
        print(f"  monitor : {monitor_path}")

    # ── Enrichissement system ──────────────────────────────────────────
    # Joint events.jsonl × monitor.jsonl post-mortem : ajoute une clé
    # ``system`` à chaque row de results.jsonl (peaks dans la fenêtre
    # [row_start, row_end]) et appose un event ``bench_summary`` à la
    # fin de events.jsonl avec les phase peaks (idle / model_load /
    # global_run). Idempotent et silencieux si le monitor n'a pas tourné.
    from lib.monitor.joiner import enrich_run, format_summary_console
    summary = enrich_run(run_dir)
    if summary.get("samples_count", 0) > 0:
        print()
        print(format_summary_console(summary))

    return 0


# ── Sub-builders ────────────────────────────────────────────────────────

def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--corpus", default=DEFAULT_CORPUS_SLUG,
                   help=f"Slug du corpus sous corpora/<slug>/ (défaut : {DEFAULT_CORPUS_SLUG}).")
    p.add_argument("--source", default="voxtral-transcribe",
                   choices=["voxtral-transcribe", "voxtral-chat", "whisper-cpp"],
                   help="Source de transcription. voxtral-transcribe : mode canonique "
                        "Mistral (apply_transcription_request, prompt ignoré). "
                        "voxtral-chat : mode Audio QA (apply_chat_template, prompt = "
                        "instruction utilisateur). whisper-cpp : baseline.")
    p.add_argument("--dtype", default="float16", choices=["float16", "float32"],
                   help="Précision Voxtral (ignoré pour whisper-cpp).")
    p.add_argument("--cpu", action="store_true",
                   help="Forcer CPU pour Voxtral (skip DirectML).")
    p.add_argument("--regimes", default="all",
                   help="Liste virgulée (ex. V1_raw,V3_fidele) ou 'all' (défaut).")
    p.add_argument("--limit", type=int, default=0,
                   help="N premiers samples du corpus (0 = tous).")
    p.add_argument("--max-new-tokens", type=int, default=0,
                   help="Plafond gen Voxtral (0 = adaptive selon audio_s).")
    p.add_argument("--inter-row-sleep", type=float, default=0.5,
                   help="Pause (s) entre rows pour cleanup VRAM (défaut 0.5).")
    p.add_argument("--skip-monitor", action="store_true",
                   help="Ne pas lancer le monitor PowerShell GPU/RAM en background.")
    p.add_argument("--skip-judge", action="store_true",
                   help="Skip le juge LLM. Utile pour metrics rapides.")
    p.add_argument("--judge", default="gemini",
                   choices=["claude", "gemini"],
                   help="Juge LLM per-row. 'gemini' (défaut) est multimodal "
                        "et écoute l'audio. 'claude' est purement textuel.")
    p.add_argument("--row-model", default="",
                   help="Modèle pour le juge per-row. Si vide, le défaut du "
                        "juge sélectionné s'applique (claude-haiku-4-5 ou "
                        "gemini-3.5-flash).")
    p.add_argument("--run-name", default="",
                   help="Nom du run (défaut : voxtral-poc-YYYY-MM-DD-HHMM).")
    return p.parse_args()


def _start_monitor(out_path: Path) -> subprocess.Popen | None:
    """Lance le script PowerShell GPU/RAM monitor en background, écrit
    JSONL sample par 500ms. Retourne le Popen handle pour kill ultérieur,
    ou None si pwsh est introuvable."""
    if not MONITOR_SCRIPT.exists():
        print(f"  ⚠ monitor script absent : {MONITOR_SCRIPT}", file=sys.stderr)
        return None
    try:
        proc = subprocess.Popen(
            ["pwsh", "-NoProfile", "-File", str(MONITOR_SCRIPT),
             "-OutFile", str(out_path), "-IntervalMs", "500"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        # Sécurité : kill du monitor si le bench crash sans cleanup propre.
        atexit.register(_stop_monitor, proc)
        return proc
    except FileNotFoundError:
        print(f"  ⚠ pwsh introuvable, monitor désactivé", file=sys.stderr)
        return None


def _stop_monitor(proc: subprocess.Popen | None) -> None:
    if proc is None:
        return
    try:
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=3)
            except subprocess.TimeoutExpired:
                proc.kill()
    except Exception:
        pass


def _load_regimes(only: str, path: Path) -> dict[str, dict]:
    with path.open("rb") as f:
        all_regimes = tomllib.load(f)
    if only == "all":
        return all_regimes
    wanted = {c.strip() for c in only.split(",") if c.strip()}
    missing = wanted - all_regimes.keys()
    if missing:
        print(f"FATAL : régimes inconnus {missing}. Dispo dans {path.name} : "
              f"{list(all_regimes.keys())}", file=sys.stderr)
        sys.exit(1)
    return {k: v for k, v in all_regimes.items() if k in wanted}


def _build_source(args: argparse.Namespace):
    if args.source == "voxtral-transcribe":
        from lib.sources.voxtral_transcribe import VoxtralTranscribeSource
        return VoxtralTranscribeSource(dtype=args.dtype, cpu=args.cpu)
    if args.source == "voxtral-chat":
        from lib.sources.voxtral_chat import VoxtralChatSource
        return VoxtralChatSource(dtype=args.dtype, cpu=args.cpu)
    if args.source == "whisper-cpp":
        from lib.sources.whisper_cpp import WhisperCppSource
        return WhisperCppSource()
    raise ValueError(f"source inconnue : {args.source}")


def _build_judge(args: argparse.Namespace):
    """Instancie le juge selon ``args.judge``. Retourne None si la clé
    API correspondante est absente — bench continue sans juge avec un
    warning."""
    import os
    judge_name = args.judge
    prompt_path = JUDGE_PROMPTS_DIR / f"{judge_name}_per_row.md"
    if not prompt_path.exists():
        print(
            f"  ⚠ prompt absent : {prompt_path.name} — judge skipped.",
            file=sys.stderr,
        )
        return None

    # Kwargs communs : on ne passe row_model que s'il a été explicitement
    # fourni, sinon on laisse le default du juge s'appliquer.
    kwargs: dict[str, Any] = {
        "row_system_prompt": prompt_path.read_text(encoding="utf-8"),
    }
    if args.row_model:
        kwargs["row_model"] = args.row_model

    if judge_name == "claude":
        if not os.environ.get("ANTHROPIC_API_KEY"):
            print(
                "  ⚠ ANTHROPIC_API_KEY absente — judge skipped.\n"
                "    Créer un fichier benchmark/.env avec :\n"
                "      ANTHROPIC_API_KEY=sk-ant-xxx"
            )
            return None
        from lib.judges.claude import ClaudeJudge
        return ClaudeJudge(**kwargs)

    if judge_name == "gemini":
        if not os.environ.get("GEMINI_API_KEY"):
            print(
                "  ⚠ GEMINI_API_KEY absente — judge skipped.\n"
                "    Créer un fichier benchmark/.env avec :\n"
                "      GEMINI_API_KEY=AIza...\n"
                "    Clé à générer sur https://aistudio.google.com/apikey"
            )
            return None
        from lib.judges.gemini import GeminiJudge
        return GeminiJudge(**kwargs)

    raise ValueError(f"Juge inconnu : {judge_name}")


def _build_row(*, sample, source_name, source_label, regime_name,
               regime_cfg, trans) -> dict:
    return {
        "audio_id":        sample.id,
        "audio_file":      sample.audio_path.name,
        "audio_seconds":   sample.duration_s,
        "tier":            sample.tier,
        "reference_text":  sample.reference_text,
        "reference_words": sample.reference_words,
        "source":          source_name,
        "source_label":    source_label,
        "regime":          regime_name,
        "regime_label":    regime_cfg.get("label", regime_name),
        "regime_prompt":   regime_cfg.get("system_prompt", ""),
        "ok":              trans.ok,
        "error":           trans.error,
        "text":            trans.text,
        "elapsed_s":       trans.elapsed_s,
        "rtf":             trans.rtf,
        "generated_tokens": trans.generated_tokens,
        "extras":          trans.extras,
        "timestamp":       datetime.now().isoformat(timespec="seconds"),
    }


def _compute_metrics(*, hypothesis: str, reference: str) -> dict:
    er = metric_wer.compute(reference, hypothesis)
    lo = metric_looping.compute(hypothesis)
    lk = metric_leak.detect(hypothesis)
    return {
        "wer":               None if math.isnan(er.wer) else er.wer,
        "cer":               None if math.isnan(er.cer) else er.cer,
        "looping_score":     lo.score,
        "longest_ngram":     list(lo.longest_ngram),
        "hallucination_hits": lk.hallucinations,
        "custom_leak_hits":  lk.custom_leaks,
        "char_count":        len(hypothesis),
        "word_count":        len(hypothesis.split()),
    }


def _print_summary(row: dict, *, dt: float) -> None:
    if not row["ok"]:
        print(f"FAIL {row['error'][:80]}")
        return
    m = row.get("metrics") or {}
    wer = m.get("wer")
    wer_s = "n/a" if wer is None else f"{wer:.2f}"
    judge = row.get("judge") or {}
    axes = judge.get("axes") or {} if isinstance(judge, dict) else {}
    fid = axes.get("fidelite_signal", "-")
    pro = axes.get("proprete", "-")
    halu = axes.get("absence_hallucination", "-")
    reg = axes.get("regime_respecte", "-")
    parts = [
        f"RTF {row['rtf']:.2f}",
        f"wer {wer_s}",
        f"{row['generated_tokens']:>4}tok",
    ]
    if judge:
        parts.append(f"j {fid}/{pro}/{halu}/{reg}")
    parts.append(f"[{dt:.1f}s]")
    print(" ".join(parts))


if __name__ == "__main__":
    sys.exit(main())
