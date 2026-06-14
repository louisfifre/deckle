"""Synthesize the five French audition sentences with F5-TTS, pure ONNX Runtime.

F5-TTS is a zero-shot flow-matching voice-cloning model. We use the French
fine-tune RASPIAUDIO/F5-French-MixedSpeakers-reduced, exported once to three ONNX
graphs (Preprocess / Transformer / Decode) by Export_F5.py from DakeQQ/F5-TTS-ONNX.
Inference here is pure onnxruntime — no torch, no transformers.

The model clones a reference voice: we feed an EXISTING French clip from the run dir
(onnx_piper_siwis_01_neutre.wav) plus its exact transcript (the 01_neutre sentence)
as ref_audio + ref_text, then generate each target sentence in that timbre.

We bypass the bundled Chinese convert_char_to_pinyin helper and feed French text
directly: this is a character-level Latin vocab, so each character maps to an id.

Run with the dedicated venv:
    benchmark\\.venv-f5\\Scripts\\python.exe benchmark\\benches\\tts-audition\\f5_synth.py
"""

from __future__ import annotations

import sys
import time
import traceback
import wave
from pathlib import Path

import numpy as np
import onnxruntime as ort

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / "runs" / "tts-audition-poc-0001"

MODEL_DIR = Path(r"D:\models\tts\f5-fr")
VOCAB_PATH = MODEL_DIR / "vocab.txt"
ONNX_DIR = MODEL_DIR / "onnx"
PRE_PATH = ONNX_DIR / "F5_Preprocess.onnx"
TRANS_PATH = ONNX_DIR / "F5_Transformer.onnx"
DEC_PATH = ONNX_DIR / "F5_Decode.onnx"

# Voice-clone reference: an existing French clip in the run dir whose exact
# transcript is the 01_neutre sentence below.
REF_AUDIO = OUT / "onnx_piper_siwis_01_neutre.wav"

SAMPLE_RATE = 24000
HOP_LENGTH = 256
NFE_STEP = 32          # denoising steps the transformer was exported for
FUSE_NFE = 1
SPEED = 1.0
RANDOM_SEED = 9527

SENTENCES = {
    "01_neutre": ("Bonjour Louis. Voici la réponse que tu cherchais : il te suffit "
                  "d'appuyer sur le raccourci, et je te lis la suite à voix haute."),
    "02_explication": ("Alors, pour résumer simplement : le modèle tourne en local, "
                       "sur ta carte graphique, sans jamais rien envoyer dans le cloud."),
    "03_emotion": ("Franchement, c'est génial ! Ça marche du premier coup, "
                   "je n'en reviens pas."),
    "04_tics": ("Euh… attends, du coup, comment dire… ouais voilà, "
                "c'est exactement ça en fait."),
    "05_question": "Tu veux que je te lise la suite, ou bien je m'arrête là ?",
}

REF_TEXT = SENTENCES["01_neutre"]


def log(m: str) -> None:
    print(m, flush=True)


def load_vocab(path: Path) -> dict[str, int]:
    vocab: dict[str, int] = {}
    with open(path, "r", encoding="utf-8") as f:
        for i, char in enumerate(f):
            vocab[char[:-1]] = i  # strip the trailing newline only (matches the export)
    return vocab


# Normalize typographic variants to forms present in the vocab (mirror of the
# custom_trans table in the bundled convert_char_to_pinyin).
_TRANS = str.maketrans({
    "’": "'",   # right single quote -> straight apostrophe
    "‘": "'",
    "“": '"',
    "”": '"',
    ";": ",",
})


def text_to_ids(text: str, vocab: dict[str, int]) -> np.ndarray:
    """French text -> int32 ids, character level, bypassing the pinyin helper."""
    text = text.translate(_TRANS)
    ids = [vocab.get(c, 0) for c in text]
    return np.asarray([ids], dtype=np.int32)


def read_wav_mono_24k(path: Path) -> np.ndarray:
    """Read a WAV as float32 mono at 24 kHz. Assumes the run-dir clips are 16-bit;
    resamples linearly if the rate differs."""
    with wave.open(str(path), "rb") as w:
        n_ch = w.getnchannels()
        sw = w.getsampwidth()
        sr = w.getframerate()
        frames = w.readframes(w.getnframes())
    if sw != 2:
        raise ValueError(f"{path.name}: expected 16-bit PCM, got sampwidth={sw}")
    data = np.frombuffer(frames, dtype=np.int16).astype(np.float32) / 32768.0
    if n_ch > 1:
        data = data.reshape(-1, n_ch).mean(axis=1)
    if sr != SAMPLE_RATE:
        # Linear resample to 24 kHz.
        n_out = int(round(len(data) * SAMPLE_RATE / sr))
        x_old = np.linspace(0.0, 1.0, num=len(data), endpoint=False)
        x_new = np.linspace(0.0, 1.0, num=n_out, endpoint=False)
        data = np.interp(x_new, x_old, data).astype(np.float32)
    return data


def make_session(path: Path) -> ort.InferenceSession:
    so = ort.SessionOptions()
    so.log_severity_level = 4
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return ort.InferenceSession(str(path), sess_options=so,
                               providers=["CPUExecutionProvider"])


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    for p in (PRE_PATH, TRANS_PATH, DEC_PATH, VOCAB_PATH, REF_AUDIO):
        if not p.exists():
            log(f"MISSING input: {p}")
            return 2

    ort.set_seed(RANDOM_SEED)
    vocab = load_vocab(VOCAB_PATH)
    log(f"vocab size: {len(vocab)}")

    log("Loading ONNX sessions...")
    sess_pre = make_session(PRE_PATH)
    sess_trans = make_session(TRANS_PATH)
    sess_dec = make_session(DEC_PATH)

    # Introspect the actual graph IO names rather than assuming them.
    pre_in = [i.name for i in sess_pre.get_inputs()]
    pre_out = [o.name for o in sess_pre.get_outputs()]
    trans_in = [i.name for i in sess_trans.get_inputs()]
    trans_out = [o.name for o in sess_trans.get_outputs()]
    dec_in = [i.name for i in sess_dec.get_inputs()]
    dec_out = [o.name for o in sess_dec.get_outputs()]
    log(f"Preprocess  in={pre_in}\n            out={pre_out}")
    log(f"Transformer in={trans_in}\n            out={trans_out}")
    log(f"Decode      in={dec_in}\n            out={dec_out}")

    pre_audio_dtype = sess_pre.get_inputs()[0].type  # tensor(float) or tensor(int16)

    # Reference audio (voice to clone).
    ref = read_wav_mono_24k(REF_AUDIO)
    if "int16" in pre_audio_dtype:
        ref_arr = (ref * 32768.0).clip(-32768, 32767).astype(np.int16)
    else:
        ref_arr = ref.astype(np.float32)
    ref_arr = ref_arr.reshape(1, 1, -1)
    ref_audio_len = ref.shape[0] // HOP_LENGTH + 1

    # Byte-length heuristic for duration budgeting, as in the upstream inference.
    ref_text_bytes = len(REF_TEXT.encode("utf-8"))

    produced: list[str] = []
    for sid, text in SENTENCES.items():
        dest = OUT / f"onnx_f5_fr_{sid}.wav"
        if dest.exists():
            log(f"skip: {dest.name}")
            produced.append(dest.name)
            continue
        try:
            t0 = time.time()
            gen_text_bytes = len(text.encode("utf-8"))
            max_duration = np.array(
                [ref_audio_len + int(ref_audio_len / ref_text_bytes * gen_text_bytes / SPEED)],
                dtype=np.int64,
            )
            # Concatenate ref + gen text, character level.
            text_ids = text_to_ids(REF_TEXT + text, vocab)

            # --- Preprocess ---
            pre_feeds = {pre_in[0]: ref_arr, pre_in[1]: text_ids, pre_in[2]: max_duration}
            pre_vals = sess_pre.run(pre_out, pre_feeds)
            (noise, rope_cos_q, rope_sin_q, rope_cos_k, rope_sin_k,
             cat_mel_text, cat_mel_text_drop, ref_signal_len) = pre_vals

            # --- Transformer (denoising loop) ---
            time_step = np.array([0], dtype=np.int32)
            for _ in range(0, NFE_STEP - 1, FUSE_NFE):
                noise, time_step = sess_trans.run(
                    trans_out,
                    {
                        trans_in[0]: noise,
                        trans_in[1]: rope_cos_q,
                        trans_in[2]: rope_sin_q,
                        trans_in[3]: rope_cos_k,
                        trans_in[4]: rope_sin_k,
                        trans_in[5]: cat_mel_text,
                        trans_in[6]: cat_mel_text_drop,
                        trans_in[7]: time_step,
                    },
                )

            # --- Decode (vocoder) ---
            sig = sess_dec.run(dec_out, {dec_in[0]: noise, dec_in[1]: ref_signal_len})[0]
            sig = np.asarray(sig).reshape(-1)
            if sig.dtype == np.int16:
                pcm = sig
            else:
                pcm = (np.clip(sig, -1.0, 1.0) * 32767.0).astype(np.int16)

            with wave.open(str(dest), "wb") as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(SAMPLE_RATE)
                w.writeframes(pcm.tobytes())

            dur = len(pcm) / SAMPLE_RATE
            log(f"wrote {dest.name}  ({dur:.1f}s audio, {time.time()-t0:.1f}s compute)")
            produced.append(dest.name)
        except Exception as e:  # noqa: BLE001
            log(f"GEN FAILED {sid}: {type(e).__name__}: {str(e)[:300]}")
            traceback.print_exc()

    log(f"\nDone. {len(produced)} F5 wavs.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        raise SystemExit(1)
