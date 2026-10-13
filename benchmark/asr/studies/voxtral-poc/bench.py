"""Bench voxtral-poc — valider que Voxtral DML peut remplacer Whisper.

``_parse_args`` shows the first line above as the CLI description.

For each (sample, regime) of the chosen source: transcribe, score WER and
CER against the corpus's Whisper reference, detect looping and
hallucinations, run the per-row judge (Gemini by default) unless
``--skip-judge``, and write one JSON row to ``results.jsonl``. The question
is which regime could replace Whisper plus the Ollama rewrite in Deckle.
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

# benchmark/ goes on sys.path so `lib` and `asr.lib` import without an
# install; benches stay standalone scripts.
BENCH_DIR = Path(__file__).resolve().parent
BENCHMARK_ROOT = BENCH_DIR.parent.parent
BENCHMARK_DIR = BENCHMARK_ROOT.parent
sys.path.insert(0, str(BENCHMARK_DIR))

from lib._base_compat import _ensure_stdout_utf8
from asr.lib import corpus
from lib import paths
from lib.env import load_dotenv
from lib.event_log import EventLog
from asr.lib.metrics import wer as metric_wer
from asr.lib.metrics import looping as metric_looping
from asr.lib.metrics import leak as metric_leak

MONITOR_SCRIPT = BENCHMARK_ROOT.parent / "lib" / "monitor" / "gpu_monitor.ps1"


DEFAULT_CORPUS_SLUG = "voxtral-poc"
JUDGE_PROMPTS_DIR = BENCHMARK_ROOT / "prompts" / "judges"

# One regime file per source, because valid regimes depend on the mode: a
# "translate to English" regime does nothing in canonical transcription,
# which ignores the prompt.
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
    load_dotenv(BENCHMARK_DIR / ".env")

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
    # Built before the source because it is only an HTTP client, so the ASR
    # model load below stands alone between the model_load_{start,end}
    # events that the VRAM measurement brackets.
    judge = None
    if not args.skip_judge:
        judge = _build_judge(args)
        if judge:
            print(f"  judge ready  : {judge.label} model={judge.row_model}")

    # ── Run dir ────────────────────────────────────────────────────────
    if args.run_name:
        run_dir = paths.RUNS_DIR / args.run_name
        run_dir.mkdir(parents=True, exist_ok=True)
    else:
        run_dir = paths.make_run_dir(model=args.source, phase="poc")
    results_path = run_dir / "results.jsonl"
    events_path  = run_dir / "events.jsonl"
    monitor_path = run_dir / "monitor.jsonl"
    print(f"  run_dir : {run_dir}")

    # ── PowerShell monitor ─────────────────────────────────────────────
    monitor_proc = _start_monitor(monitor_path) if not args.skip_monitor else None
    if monitor_proc:
        print(f"  monitor : pwsh PID {monitor_proc.pid} → {monitor_path.name}")
    print()

    # ── Event log ──────────────────────────────────────────────────────
    log = EventLog(events_path)
    log.event("bench_start", corpus=args.corpus, source=args.source,
              dtype=args.dtype, regimes=list(regimes.keys()),
              n_samples=len(samples), max_new_tokens=args.max_new_tokens)

    # ── Source (model load, bracketed for the VRAM measurement) ───────
    # The monitor starts slowly and samples about once a second; this pause
    # gives it an idle baseline before the model loads, so phase peaks do
    # not drown in transition noise.
    if monitor_proc:
        time.sleep(10.0)
    log.event("model_load_start", source=args.source, dtype=args.dtype)
    source = _build_source(args)
    log.event("model_load_end", source=args.source, source_label=source.label,
              n_params=getattr(source, "n_params", None))
    print(f"  source ready : {source.label}")
    # Let DirectML VRAM settle after the load and give the monitor a
    # post-load baseline before the first row's generation buffers inflate
    # the peaks.
    if monitor_proc:
        time.sleep(10.0)

    # ── Loop ───────────────────────────────────────────────────────────
    # Imported here: cleanup_gpu only serves the Voxtral sources.
    from asr.lib.sources._voxtral_common import cleanup_gpu

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
                row["metrics"] = _compute_metrics(
                    hypothesis=trans.text,
                    reference=sample.reference_text,
                )
                # audio_path always goes along: text judges ignore it, the
                # multimodal Gemini judge listens to it.
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
                # A cheap cleanup between rows helps DirectML release VRAM
                # before the next one.
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

    # ── System enrichment ──────────────────────────────────────────────
    # Adds the per-row and per-phase system peaks; writes nothing when the
    # monitor did not run.
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
                   help="Nom custom du run sous RUNS_DIR (défaut : <source>-poc-NNNN).")
    return p.parse_args()


def _start_monitor(out_path: Path) -> subprocess.Popen | None:
    """None when the script or pwsh is missing."""
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
        # Stops the monitor even when the bench crashes before its cleanup.
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
        from asr.lib.sources.voxtral_transcribe import VoxtralTranscribeSource
        return VoxtralTranscribeSource(dtype=args.dtype, cpu=args.cpu)
    if args.source == "voxtral-chat":
        from asr.lib.sources.voxtral_chat import VoxtralChatSource
        return VoxtralChatSource(dtype=args.dtype, cpu=args.cpu)
    if args.source == "whisper-cpp":
        from asr.lib.sources.whisper_cpp import WhisperCppSource
        return WhisperCppSource()
    raise ValueError(f"source inconnue : {args.source}")


def _build_judge(args: argparse.Namespace):
    """None when the judge's prompt or API key is missing: the bench then
    runs without a judge."""
    import os
    judge_name = args.judge
    prompt_path = JUDGE_PROMPTS_DIR / f"{judge_name}_per_row.md"
    if not prompt_path.exists():
        print(
            f"  ⚠ prompt absent : {prompt_path.name} — judge skipped.",
            file=sys.stderr,
        )
        return None

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
        from asr.lib.judges.claude import ClaudeJudge
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
        from asr.lib.judges.gemini import GeminiJudge
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
