"""Synthesize French TTS LOCALLY via Orpheus (ONNX) + SNAC decoder (ONNX).

On-doctrine path: no online inference, no `transformers` at inference time. The
3B Orpheus French finetune is exported once to ONNX-genai by the model builder
(export-time torch/transformers is allowed); here we drive it purely through
`onnxruntime_genai` (LM) + `onnxruntime` (SNAC decoder), both on the CPU EP.

Pipeline (Orpheus -> SNAC):
  1. LM (onnxruntime_genai): feed prompt token IDs framed by Orpheus special
     tokens, autoregress until the end-of-speech token. The model emits SNAC
     "speech" tokens (id >= CODE_OFFSET).
  2. Decode tokens to SNAC codes: code = id - CODE_OFFSET - (pos % 7) * 4096,
     keeping only the running positions; 7 tokens = 1 frame.
  3. De-interleave the 7 codes/frame into SNAC's 3 hierarchical levels
     (canonical Orpheus mapping: c0=[0]; c1=[1,4]; c2=[2,3,5,6]).
  4. SNAC `snac24_int2wav_static.onnx` is STATIC: it decodes exactly 12 frames
     (codes0[12]/codes1[24]/codes2[48]) -> 24576 samples. We window the frame
     stream in blocks of 12 and concatenate; a trailing partial block is padded
     and its extra tail trimmed.

Weights live under D:\\models\\tts\\ (LM: orpheus-fr, decoder: snac-decoder),
outside the repo. Outputs land in the gitignored run dir, skip-if-exists.

Run with the dedicated venv:
    benchmark\\.venv-orpheus\\Scripts\\python.exe ^
        benchmark\\benches\\tts-audition\\orpheus_synth.py
"""

from __future__ import annotations

import sys
import time
import traceback
from pathlib import Path

import numpy as np
import soundfile as sf
import onnxruntime as ort
import onnxruntime_genai as og

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / "runs" / "tts-audition-poc-0001"
LM_DIR = Path(r"D:\models\tts\orpheus-fr")
SNAC = Path(r"D:\models\tts\snac-decoder\snac24_int2wav_static.onnx")

# Orpheus special tokens (canonical, from canopyai/Orpheus-TTS).
SOT = 128259           # start of human turn
EOT = 128009           # <|eot_id|> end of text
EOH = 128260           # end of human turn
SOA = 128261           # start of AI turn
SOS = 128257           # start of speech
EOS_AUDIO = 128258     # end of speech (generation stop)
CODE_OFFSET = 128266   # first SNAC "speech" token id

# SNAC int2wav_static decodes a fixed window of 12 frames -> 24576 samples.
WIN_FRAMES = 12
WIN_SAMPLES = 24576
SR = 24000

# CML French finetune has no documented named voice; we keep the prompt voiceless
# (raw text). If a finetune expects a "voice: text" prefix, set VOICE accordingly.
VOICE = ""

SENTENCES = {
    "01_neutre": ("Bonjour Louis. Voici la réponse que tu cherchais : il te suffit "
                  "d'appuyer sur le raccourci, et je te lis la suite à voix haute."),
    "04_tics": ("Euh… attends, du coup, comment dire… ouais voilà, "
                "c'est exactement ça en fait."),
}


def log(m: str) -> None:
    print(m, flush=True)


def build_prompt_ids(tokenizer: "og.Tokenizer", text: str) -> list[int]:
    """Frame the text with Orpheus special tokens, returning a flat id list."""
    body = f"{VOICE}: {text}" if VOICE else text
    text_ids = list(tokenizer.encode(body))
    return [SOT] + text_ids + [EOT, EOH, SOA, SOS]


def generate_speech_tokens(model, tokenizer, prompt_ids, max_new=1800):
    """Autoregress until EOS_AUDIO (or cap); return the generated token ids."""
    params = og.GeneratorParams(model)
    params.set_search_options(
        do_sample=True, temperature=0.6, top_p=0.9, top_k=50,
        repetition_penalty=1.1, max_length=len(prompt_ids) + max_new,
    )
    gen = og.Generator(model, params)
    gen.append_tokens(prompt_ids)
    produced: list[int] = []
    while not gen.is_done():
        gen.generate_next_token()
        tok = int(gen.get_next_tokens()[0])
        if tok == EOS_AUDIO:
            break
        produced.append(tok)
        if len(produced) >= max_new:
            break
    return produced


def tokens_to_codes(tokens: list[int]) -> list[int]:
    """Keep only SNAC speech tokens and map them to raw codes via running position.

    Orpheus: code = id - CODE_OFFSET - (pos % 7) * 4096. `pos` is the running
    index over the kept speech tokens; a token is a speech token when its code
    lands in [0, 4096). Anything else (stray text/special) is skipped, and the
    position counter only advances on accepted codes so frame alignment holds.
    """
    codes: list[int] = []
    pos = 0
    for tok in tokens:
        if tok < CODE_OFFSET:
            continue
        code = tok - CODE_OFFSET - (pos % 7) * 4096
        if 0 <= code < 4096:
            codes.append(code)
            pos += 1
    return codes


def deinterleave(frame7: list[int]) -> tuple[list[int], list[int], list[int]]:
    """One 7-code frame -> (c0[1], c1[2], c2[4]) per the canonical Orpheus map."""
    c0 = [frame7[0]]
    c1 = [frame7[1], frame7[4]]
    c2 = [frame7[2], frame7[3], frame7[5], frame7[6]]
    return c0, c1, c2


def decode_codes(session: ort.InferenceSession, codes: list[int]) -> np.ndarray:
    """Window the code stream into 12-frame blocks and SNAC-decode each."""
    n_frames = len(codes) // 7
    if n_frames == 0:
        return np.zeros(0, dtype=np.float32)
    frames = [codes[7 * j: 7 * j + 7] for j in range(n_frames)]

    chunks: list[np.ndarray] = []
    for start in range(0, n_frames, WIN_FRAMES):
        block = frames[start:start + WIN_FRAMES]
        real = len(block)
        if real < WIN_FRAMES:  # pad the trailing partial window
            block = block + [[0] * 7] * (WIN_FRAMES - real)
        c0, c1, c2 = [], [], []
        for fr in block:
            a, b, c = deinterleave(fr)
            c0 += a
            c1 += b
            c2 += c
        feeds = {
            "codes0": np.asarray(c0, dtype=np.int64).reshape(1, WIN_FRAMES),
            "codes1": np.asarray(c1, dtype=np.int64).reshape(1, 2 * WIN_FRAMES),
            "codes2": np.asarray(c2, dtype=np.int64).reshape(1, 4 * WIN_FRAMES),
        }
        wav = session.run(None, feeds)[0].reshape(-1).astype(np.float32)
        if real < WIN_FRAMES:  # trim the padded tail to the real frame count
            wav = wav[: int(round(WIN_SAMPLES * real / WIN_FRAMES))]
        chunks.append(wav)
    return np.concatenate(chunks) if chunks else np.zeros(0, dtype=np.float32)


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    log(f"Output: {OUT}")
    log(f"LM:     {LM_DIR}")
    log(f"SNAC:   {SNAC}")

    if not (LM_DIR / "model.onnx").exists():
        log("  FATAL: LM export missing (model.onnx). Run the genai builder first.")
        return 2
    if not SNAC.exists():
        log("  FATAL: SNAC decoder onnx missing.")
        return 2

    log("\nLoading SNAC decoder (CPU EP) ...")
    snac = ort.InferenceSession(str(SNAC), providers=["CPUExecutionProvider"])
    for i in snac.get_inputs():
        log(f"  SNAC in  {i.name} {i.shape} {i.type}")
    for o in snac.get_outputs():
        log(f"  SNAC out {o.name} {o.shape} {o.type}")

    log("\nLoading Orpheus LM (onnxruntime_genai, CPU) ...")
    t0 = time.time()
    config = og.Config(str(LM_DIR))
    model = og.Model(config)
    tokenizer = og.Tokenizer(model)
    log(f"  LM ready in {time.time() - t0:.1f}s")

    for sid, text in SENTENCES.items():
        dest = OUT / f"onnx_orpheus_{sid}.wav"
        if dest.exists():
            log(f"\nskip: {dest.name}")
            continue
        log(f"\n== {sid} ==\n  {text}")
        try:
            t0 = time.time()
            prompt_ids = build_prompt_ids(tokenizer, text)
            log(f"  prompt tokens: {len(prompt_ids)}")
            toks = generate_speech_tokens(model, tokenizer, prompt_ids)
            t_gen = time.time() - t0
            codes = tokens_to_codes(toks)
            n_frames = len(codes) // 7
            log(f"  generated {len(toks)} tokens -> {len(codes)} codes "
                f"({n_frames} frames) in {t_gen:.1f}s")
            if n_frames == 0:
                log("  GEN EMPTY: no SNAC speech tokens produced; "
                    "prompt format likely off for this finetune.")
                continue
            audio = decode_codes(snac, codes)
            peak = float(np.max(np.abs(audio))) if audio.size else 0.0
            log(f"  decoded {audio.size} samples  peak={peak:.3f}")
            if peak > 0:
                audio = audio / max(peak, 1e-6) * 0.95  # gentle normalize
            sf.write(str(dest), audio.astype(np.float32), SR)
            log(f"  wrote {dest.name}  ({SR} Hz, {audio.size / SR:.1f}s)")
        except Exception as e:  # noqa: BLE001
            log(f"  GEN FAILED {sid}: {type(e).__name__}: {str(e)[:240]}")
            traceback.print_exc()

    wavs = sorted(OUT.glob("onnx_orpheus_*.wav"))
    log(f"\nDone. {len(wavs)} orpheus wavs:")
    for w in wavs:
        log(f"  {w.name}  ({w.stat().st_size // 1024} KB)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        raise SystemExit(1)
