"""POC d'évaluation Voxtral — 6 configs (Whisper baseline + 5 Voxtral).

Phase 1 du POC formalisé par ADR-0011. Sur chaque fichier audio du
mini-corpus, exécute :

  - W0 : Whisper baseline via wrapper subprocess vers whisper-cli.exe
  - V1 raw         : prompt système minimal, transcription brute
  - V2 lissé       : prompt système qui autorise corrections de surface
  - V3 fidèle      : prompt système verbatim strict
  - V4 fidèle ann. : prompt système verbatim + annotations crochets
  - V5 traduit EN  : prompt système traduction directe vers l'anglais

Pour chaque sortie : métriques objectives (RTF, bouclage n-gramme,
hallucinations connues, prompt leak), puis scoring qualitatif via
Ollama judge (Mistral-family local, par défaut ``ministral-3:14b``).

Sortie : un JSONL ``results.jsonl`` + un snapshot des configs/prompts
sous ``benchmark/runs/voxtral-poc-YYYY-MM-DD-HHMM/``. Le rapport
humain n'est PAS généré par ce bench — il est rédigé en session
Claude Code à partir des JSONL.

Usage :
    python voxtral_bench.py                       # mini-corpus (warm-up only)
    python voxtral_bench.py --audio path\to.wav   # single file ad hoc
    python voxtral_bench.py --skip-baseline       # pas de W0 Whisper
    python voxtral_bench.py --skip-judge          # pas de scoring Ollama
    python voxtral_bench.py --limit 3 --verbose

Référence : ADR-0011 (POC évaluation Voxtral).
"""

from __future__ import annotations

import argparse
import io
import json
import os
import shutil
import sys
import time
import tomllib
from dataclasses import asdict
from datetime import datetime
from pathlib import Path

# Force UTF-8 stdout/stderr on Windows so accented output survives
# terminal redirection.
if sys.stdout.encoding != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
if sys.stderr.encoding != "utf-8":
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

BENCHMARK_DIR = Path(__file__).resolve().parent
REPO_ROOT     = BENCHMARK_DIR.parent
sys.path.insert(0, str(BENCHMARK_DIR))

# Lib helpers — imported here so a missing dependency surfaces with a
# clear traceback at module-load time, not deep inside the loop.
from lib.voxtral_engine            import DEFAULT_MODEL_ID, VoxtralEngine
from lib.voxtral_judge             import (DEFAULT_JUDGE_MODEL, load_judge_system_prompt,
                                           score as judge_score)
from lib.voxtral_metrics           import compute as compute_metrics
from lib.voxtral_baseline_whisper  import run as run_whisper_baseline


# ── Chemins canoniques ────────────────────────────────────────────────
CONFIG_DIR        = BENCHMARK_DIR / "config"
PROMPTS_TOML      = CONFIG_DIR / "voxtral_prompts.toml"
JUDGE_PROMPT_FILE = CONFIG_DIR / "prompts" / "voxtral_judge_system_prompt.txt"
WHISPER_PROMPT_FILE = CONFIG_DIR / "prompts" / "whisper_initial_prompt.txt"

RUNS_DIR          = BENCHMARK_DIR / "runs"
CORPUS_DIR        = BENCHMARK_DIR / "corpus" / "voxtral-poc"
DEFAULT_WARMUP    = REPO_ROOT / "src" / "Deckle.App" / "Assets" / "Sounds" / "speech.wav"


# ── Argparse ──────────────────────────────────────────────────────────
def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--audio", type=Path, default=None,
                   help="Fichier audio unique à transcrire (court-circuite le corpus). "
                        "Si omis, charge corpus/voxtral-poc/*.wav + l'audio warm-up.")
    p.add_argument("--limit", type=int, default=None,
                   help="Plafonne le nombre d'audios traités (debug).")
    p.add_argument("--configs", default="all",
                   help="Liste virgulée des configs Voxtral à tourner (V1_raw,V2_lisse,...). "
                        "Défaut : toutes celles du TOML. W0 est contrôlé par --skip-baseline.")
    p.add_argument("--skip-baseline", action="store_true",
                   help="Skip W0 (Whisper baseline). Utile si whisper-cli.exe est absent.")
    p.add_argument("--skip-judge", action="store_true",
                   help="Skip le scoring Ollama. Utile pour un dry-run rapide.")
    p.add_argument("--judge-model", default=DEFAULT_JUDGE_MODEL,
                   help=f"Modèle Ollama judge (défaut : {DEFAULT_JUDGE_MODEL}).")
    p.add_argument("--cpu", action="store_true",
                   help="Forcer l'inférence Voxtral CPU (skip GPU même si dispo).")
    p.add_argument("--verbose", action="store_true",
                   help="Affiche les transcriptions en cours d'exécution.")
    p.add_argument("--run-name", default=None,
                   help="Nom du run (défaut : voxtral-poc-YYYY-MM-DD-HHMM).")
    return p.parse_args()


# ── Découverte du corpus ──────────────────────────────────────────────
def discover_corpus(*, single_audio: Path | None, limit: int | None) -> list[Path]:
    if single_audio is not None:
        if not single_audio.exists():
            print(f"FATAL: audio introuvable — {single_audio}", file=sys.stderr)
            sys.exit(1)
        return [single_audio]

    audios: list[Path] = []
    if CORPUS_DIR.exists():
        audios.extend(sorted(CORPUS_DIR.glob("*.wav")))
        audios.extend(sorted(CORPUS_DIR.glob("*.flac")))
    if DEFAULT_WARMUP.exists() and DEFAULT_WARMUP not in audios:
        audios.append(DEFAULT_WARMUP)

    if not audios:
        print(f"FATAL: pas d'audio trouvé. Attendu sous {CORPUS_DIR} ou {DEFAULT_WARMUP}",
              file=sys.stderr)
        sys.exit(1)

    if limit is not None:
        audios = audios[:limit]
    return audios


# ── Chargement des prompts Voxtral depuis TOML ────────────────────────
def load_voxtral_configs(*, only: str) -> dict[str, dict]:
    with PROMPTS_TOML.open("rb") as f:
        all_configs = tomllib.load(f)

    if only == "all":
        return all_configs
    wanted = {c.strip() for c in only.split(",") if c.strip()}
    missing = wanted - all_configs.keys()
    if missing:
        print(f"FATAL: configs inconnues — {missing}. Dispo : {list(all_configs.keys())}",
              file=sys.stderr)
        sys.exit(1)
    return {k: v for k, v in all_configs.items() if k in wanted}


# ── Préparation du dossier de run ─────────────────────────────────────
def prepare_run_dir(run_name: str | None) -> Path:
    name = run_name or "voxtral-poc-" + datetime.now().strftime("%Y-%m-%d-%H%M")
    run_dir = RUNS_DIR / name
    (run_dir / "snapshot").mkdir(parents=True, exist_ok=True)
    return run_dir


def snapshot_configs(run_dir: Path) -> None:
    snap = run_dir / "snapshot"
    shutil.copy2(PROMPTS_TOML,        snap / PROMPTS_TOML.name)
    shutil.copy2(JUDGE_PROMPT_FILE,   snap / JUDGE_PROMPT_FILE.name)
    if WHISPER_PROMPT_FILE.exists():
        shutil.copy2(WHISPER_PROMPT_FILE, snap / WHISPER_PROMPT_FILE.name)


# ── Exécution ─────────────────────────────────────────────────────────
def main() -> None:
    args = parse_args()

    audios = discover_corpus(single_audio=args.audio, limit=args.limit)
    voxtral_configs = load_voxtral_configs(only=args.configs)
    judge_system = load_judge_system_prompt(JUDGE_PROMPT_FILE) if not args.skip_judge else ""
    whisper_initial_prompt = (WHISPER_PROMPT_FILE.read_text(encoding="utf-8").strip()
                              if WHISPER_PROMPT_FILE.exists() else "")

    run_dir = prepare_run_dir(args.run_name)
    snapshot_configs(run_dir)
    results_path = run_dir / "results.jsonl"

    print(f"=== Voxtral POC bench — Phase 1 ===")
    print(f"  Audios          : {len(audios)}")
    print(f"  Configs Voxtral : {list(voxtral_configs.keys())}")
    print(f"  Baseline W0     : {'skipped' if args.skip_baseline else 'enabled'}")
    print(f"  Judge Ollama    : {'skipped' if args.skip_judge else args.judge_model}")
    print(f"  Run dir         : {run_dir}")
    print()

    # Voxtral engine : loaded once, reused across configs and audios.
    engine = VoxtralEngine(device="cpu" if args.cpu else None)

    total_rows = 0
    t_total0 = time.time()
    with results_path.open("w", encoding="utf-8") as out:
        for ai, audio in enumerate(audios, start=1):
            print(f"[{ai}/{len(audios)}] {audio.name}")

            # W0 — Whisper baseline
            if not args.skip_baseline:
                print(f"  W0 (whisper-cli)…", end=" ", flush=True)
                w0 = run_whisper_baseline(
                    audio_path=audio,
                    initial_prompt=whisper_initial_prompt,
                )
                row = _row_whisper(audio=audio, w0=w0,
                                   judge_system=judge_system,
                                   judge_model=args.judge_model,
                                   skip_judge=args.skip_judge)
                _write_row(out, row)
                total_rows += 1
                _print_summary(row, verbose=args.verbose)
            else:
                print(f"  W0 (whisper-cli)… skipped")

            # V1..V5 — Voxtral configs
            for cname, cfg in voxtral_configs.items():
                print(f"  {cname} ({cfg.get('label', '')})…", end=" ", flush=True)
                try:
                    vres = engine.transcribe(
                        audio_path=audio,
                        config_name=cname,
                        system_prompt=cfg["system_prompt"],
                    )
                    row = _row_voxtral(audio=audio, cfg_name=cname, cfg=cfg, vres=vres,
                                       device=engine.device,
                                       judge_system=judge_system,
                                       judge_model=args.judge_model,
                                       skip_judge=args.skip_judge)
                except Exception as e:
                    row = _row_voxtral_error(audio=audio, cfg_name=cname, cfg=cfg, err=e)
                _write_row(out, row)
                total_rows += 1
                _print_summary(row, verbose=args.verbose)

    elapsed = time.time() - t_total0
    print()
    print(f"✓ Bench terminé. {total_rows} rows en {elapsed:.1f}s")
    print(f"  JSONL : {results_path}")
    print(f"  Snapshot configs : {run_dir / 'snapshot'}")


# ── Construction des rows JSONL ───────────────────────────────────────
def _row_whisper(*, audio, w0, judge_system, judge_model, skip_judge):
    metrics = compute_metrics(w0.text) if w0.ok else None
    judge = None
    if w0.ok and not skip_judge and w0.text:
        judge = judge_score(
            transcription=w0.text,
            config_name="W0",
            config_label="Whisper baseline (whisper-cli + initial prompt actif)",
            judge_system=judge_system,
            model=judge_model,
        )
    return {
        "audio":           str(audio),
        "audio_seconds":   w0.audio_seconds,
        "config":          "W0",
        "config_label":    "Whisper baseline (whisper-cli)",
        "engine":          "whisper.cpp",
        "device":          "vulkan",         # convention; whisper-cli decides at link time
        "ok":              w0.ok,
        "error":           w0.error or "",
        "text":            w0.text,
        "elapsed_seconds": w0.elapsed_seconds,
        "rtf":             w0.rtf,
        "metrics":         (asdict(metrics) if metrics else None),
        "judge":           (asdict(judge)   if judge   else None),
        "timestamp":       datetime.now().isoformat(timespec="seconds"),
    }


def _row_voxtral(*, audio, cfg_name, cfg, vres, device, judge_system, judge_model, skip_judge):
    metrics = compute_metrics(vres.text)
    judge = None
    if not skip_judge and vres.text:
        judge = judge_score(
            transcription=vres.text,
            config_name=cfg_name,
            config_label=cfg.get("label", cfg_name),
            judge_system=judge_system,
            model=judge_model,
        )
    return {
        "audio":            str(audio),
        "audio_seconds":    vres.audio_seconds,
        "config":           cfg_name,
        "config_label":     cfg.get("label", ""),
        "config_desc":      cfg.get("description", ""),
        "engine":           "voxtral",
        "model_id":         DEFAULT_MODEL_ID,
        "device":           device,
        "ok":               True,
        "text":             vres.text,
        "elapsed_seconds":  vres.elapsed_seconds,
        "rtf":              vres.rtf,
        "generated_tokens": vres.generated_tokens,
        "metrics":          asdict(metrics),
        "judge":            (asdict(judge) if judge else None),
        "timestamp":        datetime.now().isoformat(timespec="seconds"),
    }


def _row_voxtral_error(*, audio, cfg_name, cfg, err):
    return {
        "audio":         str(audio),
        "config":        cfg_name,
        "config_label":  cfg.get("label", ""),
        "engine":        "voxtral",
        "ok":            False,
        "error":         f"{type(err).__name__}: {err}",
        "timestamp":     datetime.now().isoformat(timespec="seconds"),
    }


def _write_row(fp, row: dict) -> None:
    fp.write(json.dumps(row, ensure_ascii=False) + "\n")
    fp.flush()


def _print_summary(row: dict, *, verbose: bool) -> None:
    if not row.get("ok"):
        print(f"FAIL  {row.get('error', '')[:160]}")
        return
    rtf = row.get("rtf")
    rtf_s = f"RTF {rtf:.2f}" if isinstance(rtf, (int, float)) else "RTF n/a"
    txt = row.get("text", "")
    n_chars = len(txt)
    judge = row.get("judge") or {}
    j = (f"  judge {judge.get('fidelite', '-'):>3}/{judge.get('proprete', '-'):>3}/"
         f"{judge.get('absence_leak', '-'):>3}/{judge.get('regime_respecte', '-'):>3}"
         if judge else "")
    print(f"OK  {rtf_s}  {n_chars}ch{j}")
    if verbose and txt:
        preview = txt[:200].replace("\n", " ")
        print(f"      → {preview}{'…' if len(txt) > 200 else ''}")


if __name__ == "__main__":
    main()
