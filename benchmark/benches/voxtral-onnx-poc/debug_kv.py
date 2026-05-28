"""Inspect tous les inputs du decoder + test génération sans KV cache.

Si la génération sans KV cache produit une transcription cohérente, le bug
est dans notre handling KV cache (mapping present → past, layout, dtype).
Si ça reste du charabia, le bug est ailleurs (prompt, splice, audio).
"""
from __future__ import annotations
import json
import sys
import time
from pathlib import Path
import numpy as np
import librosa
import soundfile as sf
import onnxruntime as ort
from tokenizers import Tokenizer

if sys.stdout.encoding.lower() not in {"utf-8", "utf8"}:
    sys.stdout.reconfigure(encoding="utf-8")

LOCAL = Path(r"D:\models\llm\voxtral-mini-3b-2507-onnx")
ONNX  = LOCAL / "onnx"
AUDIO = Path(r"C:\Users\Louis\AppData\Local\Deckle\benchmark\corpora\voxtral-val-30\dcad692a54fd452cbfb174ca9899deba.wav")

BOS, INST, BAUD, AUD, EINST, EOS, TRANSCRIBE = 1, 3, 25, 24, 4, 2, 34
MAX_TOKENS = 16  # court parce que no-KV = O(n²) en compute

# ─── Load minimal ────────────────────────────────────────────────────
tok = Tokenizer.from_file(str(LOCAL / "tokenizer.json"))
providers = ["DmlExecutionProvider", "CPUExecutionProvider"]
ae   = ort.InferenceSession(str(ONNX / "audio_encoder_fp16.onnx"),         providers=providers)
emb  = ort.InferenceSession(str(ONNX / "embed_tokens_fp16.onnx"),          providers=providers)
dec  = ort.InferenceSession(str(ONNX / "decoder_model_merged_fp16.onnx"),  providers=providers)
cfg  = json.loads((LOCAL / "config.json").read_text(encoding="utf-8"))
txt  = cfg.get("text_config", cfg)
nh, nkv, hd = txt["num_attention_heads"], txt["num_key_value_heads"], txt.get("head_dim", txt["hidden_size"]//txt["num_attention_heads"])

print(f"── Decoder ALL inputs ({len(dec.get_inputs())} total) ──")
for i, x in enumerate(dec.get_inputs()):
    if not ("past_key_values" in x.name and not x.name.endswith(".0.key") and not x.name.endswith(".0.value")):
        # ne pas spammer les 60 KV, juste les 2 premiers
        print(f"  [{i:2d}] {x.name:40s} {x.shape}  {x.type}")
print(f"\n── Decoder ALL outputs ({len(dec.get_outputs())} total) ──")
for i, x in enumerate(dec.get_outputs()):
    if not ("present" in x.name and not x.name.endswith(".0.key") and not x.name.endswith(".0.value")):
        print(f"  [{i:2d}] {x.name:40s} {x.shape}  {x.type}")

# ─── Audio + encoder ─────────────────────────────────────────────────
y, sr = sf.read(str(AUDIO))
if y.ndim > 1: y = y.mean(axis=1)
if sr != 16000:
    y = librosa.resample(y, orig_sr=sr, target_sr=16000)
y = np.pad(y, (0, max(0, 480000 - len(y))))[:480000].astype(np.float32)
mel = librosa.feature.melspectrogram(y=y, sr=16000, n_fft=400, hop_length=160, n_mels=128)
log_spec = np.log10(np.maximum(mel, 1e-10))
log_spec = np.maximum(log_spec, log_spec.max() - 8.0)
log_spec = ((log_spec + 4.0) / 4.0).astype(np.float32)[:, :3000][None, :]
audio_embeds = ae.run(None, {ae.get_inputs()[0].name: log_spec})[0]
if audio_embeds.ndim == 3:
    audio_embeds = audio_embeds[0]
n_audio = audio_embeds.shape[0]

# ─── Build prompt transcription canonique ───────────────────────────
suffix = tok.encode(" lang:fr [TRANSCRIBE]", add_special_tokens=False).ids
prompt = [BOS, INST, BAUD] + [AUD]*n_audio + [EINST] + suffix
print(f"\n── Prompt length: {len(prompt)} (audio={n_audio}, suffix={len(suffix)}) ──")

input_ids = np.array([prompt], dtype=np.int64)
inputs_embeds_initial = emb.run(None, {emb.get_inputs()[0].name: input_ids})[0].astype(np.float32)
inputs_embeds_initial[0, 3:3+n_audio, :] = audio_embeds.astype(np.float32)

# ─── Generation sans KV cache (full prompt + tokens generated jusqu'ici) ──
print("\n── Generation sans KV cache (full re-run each step) ──")
dec_input_names = [i.name for i in dec.get_inputs()]
kv_input_names  = [n for n in dec_input_names if "past_key_values" in n]

def empty_past(past_len: int) -> dict:
    return {n: np.zeros((1, nkv, past_len, hd), dtype=np.float16) for n in kv_input_names}

generated = []
current_embeds = inputs_embeds_initial.copy()  # (1, prompt_len, 3072)
t0 = time.perf_counter()
for step_i in range(MAX_TOKENS):
    cur_len = current_embeds.shape[1]
    feed = {
        "inputs_embeds":  current_embeds,
        "attention_mask": np.ones((1, cur_len), dtype=np.int64),
        "position_ids":   np.arange(cur_len, dtype=np.int64)[None, :],
        **empty_past(0),
    }
    outputs = dec.run(None, feed)
    logits  = outputs[0]
    next_id = int(np.argmax(logits[0, -1, :]))
    top5    = np.argsort(-logits[0, -1, :])[:5].tolist()
    top5_str = [tok.decode([t], skip_special_tokens=False) for t in top5]
    print(f"  step {step_i}: cur_len={cur_len}, next_id={next_id} {tok.decode([next_id], skip_special_tokens=False)!r}, top5={list(zip(top5, top5_str))}")
    generated.append(next_id)
    if next_id == EOS:
        print("  EOS")
        break
    # Append the new embedding for next pass.
    next_embed = emb.run(None, {emb.get_inputs()[0].name: np.array([[next_id]], dtype=np.int64)})[0].astype(np.float32)
    current_embeds = np.concatenate([current_embeds, next_embed], axis=1)

elapsed = time.perf_counter() - t0
print(f"\n── Generated {len(generated)} tokens in {elapsed:.1f}s ──")
print(f"reference  : 'Et toujours douter un peu.'")
print(f"hypothesis : {tok.decode(generated, skip_special_tokens=True)!r}")
