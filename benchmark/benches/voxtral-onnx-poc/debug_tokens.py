"""Inspect tokenizer + chat template pour résoudre H1 (IDs) et préparer H2.

Tourne sans modèle (pas de download), juste la tokenisation. Affiche les
IDs des tokens spéciaux Voxtral, encode plusieurs prompts candidats, et
décode les premiers 200 IDs pour voir la table.
"""
from __future__ import annotations
import sys
from pathlib import Path
from tokenizers import Tokenizer

if sys.stdout.encoding.lower() not in {"utf-8", "utf8"}:
    sys.stdout.reconfigure(encoding="utf-8")

LOCAL = Path(r"D:\models\llm\voxtral-mini-3b-2507-onnx")
tok = Tokenizer.from_file(str(LOCAL / "tokenizer.json"))

print("── Special tokens via tokenizer ─────────────────────────────────")
for s in ["<s>", "</s>", "[INST]", "[/INST]", "[BEGIN_AUDIO]", "[AUDIO]", "[TRANSCRIBE]"]:
    try:
        ids = tok.encode(s, add_special_tokens=False).ids
        print(f"  encode({s!r}, add_special=False) → {ids}")
    except Exception as e:
        print(f"  encode({s!r}) → FAIL {type(e).__name__}: {e}")

print("\n── First 32 vocab IDs (slow_decode) ─────────────────────────────")
for i in range(32):
    try:
        s = tok.decode([i], skip_special_tokens=False)
        print(f"  {i:3d} → {s!r}")
    except Exception:
        print(f"  {i:3d} → <decode failed>")

print("\n── Hardcoded IDs check (urroxyz convention) ─────────────────────")
for name, idx in [("BOS", 1), ("INST", 3), ("EINST", 4), ("AUDIO", 24), ("BEGIN_AUDIO", 25)]:
    s = tok.decode([idx], skip_special_tokens=False)
    print(f"  {name:12s} (id={idx}) → {s!r}")

print("\n── Encode prompts candidats (sans audio splice, vue d'ensemble) ──")
prompts = {
    "chat FR":     "Transcris l'audio.",
    "chat EN":     "Transcribe the audio.",
    "transcribe FR": "lang:fr [TRANSCRIBE]",
    "transcribe EN": "lang:en [TRANSCRIBE]",
}
for label, p in prompts.items():
    ids = tok.encode(p, add_special_tokens=False).ids
    print(f"  {label:18s} → {ids}")
    print(f"    {'':18s}   decoded back: {tok.decode(ids, skip_special_tokens=False)!r}")

# chat_template.jinja
ct_path = LOCAL / "chat_template.jinja"
if ct_path.exists():
    print(f"\n── chat_template.jinja ({ct_path.stat().st_size} B) ─────────────")
    print(ct_path.read_text(encoding="utf-8"))
else:
    print(f"\n── chat_template.jinja: ABSENT")
