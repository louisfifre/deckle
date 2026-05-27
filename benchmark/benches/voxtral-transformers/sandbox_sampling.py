"""Test sandbox — Voxtral T1 canonique avec do_sample et max_new_tokens variés.

Pivot 2026-05-27. Le run voxtral-transformers-validation-0004 a révélé
deux modes d'échec disqualifiants pour Deckle : troncature en milieu de
phrase sur audios longs (audio f11dd93e à 39s), hallucinations en queue
sur audios moyens (audio 74131937 à 65s). Hypothèses cumulables :

  - le max_new_tokens du bench (4 tokens/s d'audio) est sous-dimensionné
    vs la reco communauté HF (~7 tokens/s). Pour 39s ça donne 156 tokens
    là où il en faudrait probablement 250+. Si la troncature vient de là,
    elle disparaît en relevant le plafond.
  - le greedy decoding peut tomber dans des modes d'échec catastrophiques
    (boucles, hallucinations terminales). Le sampling à température basse
    explore une branche alternative — il ne change pas le WER moyen mais
    peut sortir le modèle de ses coincements.

Ce script teste 3 configs sur 3 audios diagnostiques (1 essai par config) :

  - baseline_greedy_max4x  : config bench actuelle (max_new = audio_s * 4)
  - bumped_max_greedy_8x   : greedy, max_new = audio_s * 8
  - sample_t02_max8x       : do_sample=True, temperature=0.2, top_p=0.95,
                             max_new = audio_s * 8

Sortie : texte transcrit pour chaque (audio, config), affichage console,
pas de judge Gemini, pas de WER calculé — lecture humaine pour voir si
les modes d'échec disparaissent.
"""
from __future__ import annotations

import math
import sys
import time
from pathlib import Path

import torch
from transformers import AutoProcessor, VoxtralForConditionalGeneration

MODEL_PATH = Path(r"D:\models\llm\voxtral\Voxtral-Mini-3B-2507-safetensors")
HF_REPO_ID = "mistralai/Voxtral-Mini-3B-2507"
CORPUS_DIR = Path(r"C:\Users\Louis\AppData\Local\Deckle\benchmark\corpora\voxtral-val-30")

AUDIOS = [
    ("f11dd93ecfab4a139bd4f94599ab8bff", 39.3, "TRONCATURE"),
    ("74131937762e487c8c415ec1edb95542", 65.1, "HALLUCINATION_QUEUE"),
    ("a66c3e9237b24b738b40de0327d6e8e1",  1.6, "HALLUCINATION_SILENCE"),
]

CONFIGS = [
    {"name": "baseline_greedy_max4x", "do_sample": False, "tokens_per_sec": 4.0},
    {"name": "bumped_max_greedy_8x",  "do_sample": False, "tokens_per_sec": 8.0},
    {"name": "sample_t02_max8x",      "do_sample": True,  "tokens_per_sec": 8.0,
                                       "temperature": 0.2, "top_p": 0.95},
]


def main() -> int:
    if sys.stdout.encoding.lower() != "utf-8":
        sys.stdout.reconfigure(encoding="utf-8")

    print(f"Loading model from {MODEL_PATH} ...", flush=True)
    t_load = time.perf_counter()
    processor = AutoProcessor.from_pretrained(MODEL_PATH)
    model = VoxtralForConditionalGeneration.from_pretrained(
        MODEL_PATH, dtype=torch.bfloat16, device_map="cuda")
    print(f"  loaded in {time.perf_counter()-t_load:.1f}s\n", flush=True)

    for audio_id, audio_s, defect in AUDIOS:
        audio_path = CORPUS_DIR / f"{audio_id}.wav"
        if not audio_path.exists():
            print(f"WARN  {audio_id} : audio file missing at {audio_path}", flush=True)
            continue
        print(f"{'='*80}")
        print(f"=== {audio_id[:8]} ({audio_s}s) -- diagnostic baseline: {defect}")
        print(f"{'='*80}", flush=True)

        for cfg in CONFIGS:
            max_new = max(128, int(math.ceil(audio_s * cfg["tokens_per_sec"])))
            inputs = processor.apply_transcription_request(
                language="fr", audio=str(audio_path), model_id=HF_REPO_ID)
            inputs = inputs.to("cuda", dtype=torch.bfloat16)
            input_tokens = inputs.input_ids.shape[1]

            gen_kwargs = {
                "max_new_tokens": max_new,
                "do_sample":      cfg["do_sample"],
            }
            if cfg["do_sample"]:
                gen_kwargs["temperature"] = cfg["temperature"]
                gen_kwargs["top_p"]       = cfg["top_p"]

            t0 = time.perf_counter()
            with torch.no_grad():
                outputs = model.generate(**inputs, **gen_kwargs)
            if torch.cuda.is_available():
                torch.cuda.synchronize()
            elapsed = time.perf_counter() - t0

            generated_tokens = outputs.shape[1] - input_tokens
            text = processor.batch_decode(
                outputs[:, input_tokens:],
                skip_special_tokens=True,
            )[0].strip()

            hit_cap = generated_tokens >= max_new
            cap_marker = "  [HIT_CAP]" if hit_cap else ""
            print(f"\n  [{cfg['name']:<24s}] max_new={max_new:<5d} "
                  f"gen={generated_tokens:<5d} elapsed={elapsed:5.1f}s{cap_marker}", flush=True)
            print(f"  | {text}", flush=True)

        print(flush=True)

    return 0


if __name__ == "__main__":
    sys.exit(main())
