"""Pré-génère les transcriptions ground-truth Gemini pour un corpus.

Lit ``corpora/<slug>/corpus.jsonl`` et, pour chaque sample dont
``payload.reference_text_gemini`` est vide, appelle ``GeminiAudioSource``
avec le régime ``V_groundtruth`` puis écrit la transcription dans le
JSONL.

Idempotent : un sample déjà annoté est skippé. On peut donc relancer le
script après une interruption ou pour combler des trous (par exemple
après une 429 qui aurait fait sauter quelques samples).

Le JSONL est réécrit atomiquement via un fichier ``corpus.jsonl.tmp``
puis renommé à la fin. En cas de crash mid-passe, le fichier d'origine
reste intact ; les samples déjà transcrits dans la passe perdue sont
sauvegardés dans un fichier de log JSONL parallèle.

Usage::

    python benchmark/pregenerate_groundtruth_gemini.py
    python benchmark/pregenerate_groundtruth_gemini.py --corpus voxtral-val-30
    python benchmark/pregenerate_groundtruth_gemini.py --dry-run
    python benchmark/pregenerate_groundtruth_gemini.py --force   # ré-annote même
                                                                   # les déjà-faits

Préalable : ``GEMINI_API_KEY`` dans ``benchmark/.env``.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import tomllib
from pathlib import Path

BENCHMARK_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(BENCHMARK_DIR))

from lib._base_compat import _ensure_stdout_utf8
from lib.env import load_dotenv


DEFAULT_CORPUS_SLUG = "voxtral-val-30"
PROMPTS_PATH        = BENCHMARK_DIR / "prompts" / "transcription" / "gemini_audio.toml"
DEFAULT_REGIME      = "V_groundtruth"


def main() -> int:
    _ensure_stdout_utf8()
    args = _parse_args()

    load_dotenv()

    corpus_dir = BENCHMARK_DIR / "corpora" / args.corpus
    jsonl_path = corpus_dir / "corpus.jsonl"
    if not jsonl_path.exists():
        print(f"FATAL : corpus {args.corpus!r} introuvable ({jsonl_path}).",
              file=sys.stderr)
        return 2

    # ── Charge régime ground-truth ─────────────────────────────────────
    with PROMPTS_PATH.open("rb") as f:
        regimes = tomllib.load(f)
    if DEFAULT_REGIME not in regimes:
        print(f"FATAL : régime {DEFAULT_REGIME!r} absent de {PROMPTS_PATH.name}",
              file=sys.stderr)
        return 2
    regime = regimes[DEFAULT_REGIME]

    # ── Lit le corpus ──────────────────────────────────────────────────
    entries = [json.loads(l) for l in jsonl_path.read_text(encoding="utf-8").splitlines()
               if l.strip()]
    print(f"=== pregenerate ground truth Gemini ===")
    print(f"  corpus  : {args.corpus} ({len(entries)} samples)")
    print(f"  régime  : {DEFAULT_REGIME}")

    to_do = []
    for e in entries:
        already = bool(str(e["payload"].get("reference_text_gemini", "")).strip())
        if already and not args.force:
            continue
        wav = corpus_dir / e["payload"]["audio_file"]
        if not wav.is_file():
            print(f"  ⚠ WAV manquant pour {e['payload']['transcription_id'][:8]} "
                  f"— skipped", file=sys.stderr)
            continue
        to_do.append(e)

    print(f"  à annoter : {len(to_do)}/{len(entries)} "
          f"({'force=True' if args.force else 'idempotent'})\n")

    if args.dry_run:
        for e in to_do:
            p = e["payload"]
            print(f"  would annotate {p['transcription_id'][:8]} "
                  f"({p['duration_seconds']:>5.1f}s, {p.get('tier_validation', '?')})")
        return 0

    if not to_do:
        print("Rien à faire.")
        return 0

    # Vérification de la clé déplacée ici — elle n'est requise que pour
    # la passe réelle, pas pour ``--dry-run`` qui doit pouvoir planifier
    # hors-ligne.
    import os
    if not os.environ.get("GEMINI_API_KEY"):
        print("FATAL : GEMINI_API_KEY non défini.\n"
              "  Créer benchmark/.env avec : GEMINI_API_KEY=AIza...",
              file=sys.stderr)
        return 2

    # ── Source Gemini ──────────────────────────────────────────────────
    from lib.sources.gemini_audio import GeminiAudioSource
    src = GeminiAudioSource(system_prompt=regime["system_prompt"])
    print(f"  source : {src.label} (model={src.model})\n")

    # ── Log d'audit (parallèle au JSONL) ───────────────────────────────
    audit_path = corpus_dir / f"groundtruth-gemini-audit-{int(time.time())}.jsonl"
    print(f"  audit log : {audit_path.name}\n")

    # ── Boucle ─────────────────────────────────────────────────────────
    ok, fail = 0, 0
    with audit_path.open("w", encoding="utf-8") as audit_f:
        for i, entry in enumerate(to_do, 1):
            p = entry["payload"]
            tid = p["transcription_id"][:8]
            wav = corpus_dir / p["audio_file"]
            print(f"  [{i}/{len(to_do)}] {tid} ({p['duration_seconds']:>5.1f}s)…",
                  end=" ", flush=True)

            trans = src.transcribe(
                audio_path = wav,
                prompt     = regime.get("prompt", "Transcris cet audio."),
            )

            if not trans.ok:
                print(f"FAIL {trans.error[:80]}")
                fail += 1
                audit_f.write(json.dumps({
                    "transcription_id": p["transcription_id"],
                    "ok":               False,
                    "error":            trans.error,
                }, ensure_ascii=False) + "\n")
                audit_f.flush()
                continue

            entry["payload"]["reference_text_gemini"] = trans.text
            ok += 1
            audit_f.write(json.dumps({
                "transcription_id": p["transcription_id"],
                "ok":               True,
                "elapsed_s":        trans.elapsed_s,
                "audio_s":          trans.audio_s,
                "tokens_output":    trans.generated_tokens,
                "text":             trans.text,
                "model":            trans.extras.get("model", ""),
                "usage":            trans.extras.get("usage", {}),
            }, ensure_ascii=False) + "\n")
            audit_f.flush()

            preview = trans.text[:80] + ("…" if len(trans.text) > 80 else "")
            print(f"OK ({trans.elapsed_s:.1f}s) {preview!r}")

    # ── Réécriture atomique du corpus.jsonl ────────────────────────────
    if ok > 0:
        tmp_path = jsonl_path.with_suffix(".jsonl.tmp")
        with tmp_path.open("w", encoding="utf-8") as fout:
            for e in entries:
                fout.write(json.dumps(e, ensure_ascii=False) + "\n")
        # ``replace`` est atomique sur Windows même si la cible existe.
        tmp_path.replace(jsonl_path)

    print()
    print(f"✓ done — {ok} OK, {fail} FAIL")
    print(f"  corpus mis à jour : {jsonl_path}")
    print(f"  audit log         : {audit_path}")
    return 0 if fail == 0 else 1


def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--corpus",  default=DEFAULT_CORPUS_SLUG,
                   help=f"Slug du corpus (défaut : {DEFAULT_CORPUS_SLUG}).")
    p.add_argument("--dry-run", action="store_true",
                   help="N'appelle pas Gemini, affiche juste la liste des "
                        "samples qui seraient annotés.")
    p.add_argument("--force",   action="store_true",
                   help="Ré-annote même les samples déjà annotés.")
    return p.parse_args()


if __name__ == "__main__":
    sys.exit(main())
