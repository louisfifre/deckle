"""Shared harness for the TTS audition — text source + execution-provider policy.

Two concerns the synth scripts all share, centralized so they stay consistent:

1. SENTENCES. A small PUBLIC set (hand-written, safe to version) plus a CORPUS
   set read from the user's PRIVATE dictation at RUNTIME. The corpus text is
   never hardcoded into any tracked file: we read `reference_text_gemini`
   (clean, capitalized) from corpus.jsonl on the fly, picked by a deterministic
   length-banded spread so the audition is reproducible without committing a
   single private word. Corpora live under %LOCALAPPDATA% (outside the worktree),
   resolved via lib.paths — non-versionable by construction.

2. EXECUTION PROVIDER. One env toggle, DECKLE_TTS_EP=cpu|dml, drives every
   ort.InferenceSession. Per the AMD DirectML ConvTranspose wall (vocoder/decoder
   graphs runtime-crash on DML with no auto-fallback), any ConvTranspose-bearing
   graph is pinned to CPU regardless of the toggle; only the big iterative
   transformer/LM graphs ride DML. This is how every script becomes GPU-ready
   while a single switch flips the whole audition between CPU and GPU.
"""

from __future__ import annotations

import datetime
import json
import os
import sys
from pathlib import Path

# Make shared benchmark helpers importable without installing a package.
_BENCH_ROOT = Path(__file__).resolve().parents[2]
_BENCHMARK_ROOT = _BENCH_ROOT.parent
if str(_BENCHMARK_ROOT) not in sys.path:
    sys.path.insert(0, str(_BENCHMARK_ROOT))

from lib.paths import corpus_dir, RUNS_DIR  # noqa: E402

CORPUS_SLUG = "voxtral-val-30"

# Clean run dir for the serial rebuild (the parallel-contaminated takes live in
# tts-audition-poc-0001; we don't mix them). Every script and the player agree
# on this one location.
RUN_DIR = RUNS_DIR / "tts-audition-poc-0002"

# ── Public sentences (hand-written — versionable, not private) ───────────────
# The like-for-like neutral set every engine speaks.
PUBLIC_SENTENCES = {
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

# Expressive tag sentences — ONLY meaningful for models with learned inline
# expression tokens (Orpheus). Supertonic reads them literally (no tag channel),
# so its script must NOT use these.
EXPRESSIVE_TAGS = {
    "tags_rire": ("Alors… <breath> franchement ? <laugh> ça a marché du premier coup. "
                  "<sigh> j'avoue, je n'y croyais pas du tout."),
    "tags_emotion": ("Oh là là, <laugh> c'est vraiment génial ! <breath> attends, "
                     "laisse-moi reprendre mon souffle… voilà."),
}


def _truncate(text: str, max_chars: int) -> str:
    """Trim to <= max_chars, cutting at the last sentence-ending punctuation
    (deterministic). Keeps corpus samples a sane utterance length."""
    text = " ".join(text.split())
    if len(text) <= max_chars:
        return text
    head = text[:max_chars]
    cut = max(head.rfind(". "), head.rfind("? "), head.rfind("! "), head.rfind("… "))
    return (head[: cut + 1] if cut > max_chars // 2 else head).strip()


def corpus_rows(slug: str = CORPUS_SLUG):
    """All corpus rows as a list of payload dicts (private — runtime only)."""
    p = corpus_dir(slug) / "corpus.jsonl"
    rows = []
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line:
            rows.append(json.loads(line)["payload"])
    return rows


def corpus_sentences(slug: str = CORPUS_SLUG, n: int = 6,
                     min_chars: int = 45, max_chars: int = 200) -> dict[str, str]:
    """Pick `n` real French sentences from the private corpus, deterministically.

    Reads `reference_text_gemini` (clean), keeps those within a length band, and
    selects an even spread across that band (short -> long) for variety. The
    selection is reproducible run-to-run; the TEXT is never persisted to a
    tracked file. IDs (corpus_01..) are versionable; the values are not.
    """
    cands = []
    seen = set()
    for p in corpus_rows(slug):
        t = _truncate((p.get("reference_text_gemini") or "").strip(), max_chars)
        if min_chars <= len(t) <= max_chars and t not in seen:
            seen.add(t)
            cands.append(t)
    cands.sort(key=len)
    if not cands:
        return {}
    if len(cands) <= n:
        picks = cands
    else:
        idx = sorted({round(i * (len(cands) - 1) / (n - 1)) for i in range(n)})
        picks = [cands[i] for i in idx]
    return {f"corpus_{i + 1:02d}": t for i, t in enumerate(picks)}


def providers(*, convtranspose: bool) -> list[str]:
    """ONNX Runtime provider list for one session, from DECKLE_TTS_EP (cpu|dml).

    ConvTranspose graphs are ALWAYS CPU-pinned (AMD DirectML 80070057 wall, no
    auto-fallback). Everything else rides DML when the toggle is 'dml', with CPU
    as a genuine load-time fallback for any op DML doesn't register. If the
    onnxruntime-directml wheel isn't installed, requesting DML degrades to CPU.
    """
    ep = os.environ.get("DECKLE_TTS_EP", "cpu").strip().lower()
    if ep == "dml" and not convtranspose:
        return ["DmlExecutionProvider", "CPUExecutionProvider"]
    return ["CPUExecutionProvider"]


def stats_record(model: str, voice: str, **fields) -> None:
    """Append one timestamped run record to <run>/_stats.jsonl.

    The player reads this back into a stats panel — so every synthesis run
    self-documents (when, which EP, how fast), and the timestamp tells Louis at
    a glance whether what he's looking at is fresh. `fields` typically carries
    ep, n (sentence count), compute_s, audio_s, rtf, load_s.
    """
    rec = {"model": model, "voice": voice,
           "ts": datetime.datetime.now().isoformat(timespec="seconds"), **fields}
    p = RUN_DIR / "_stats.jsonl"
    p.parent.mkdir(parents=True, exist_ok=True)
    with p.open("a", encoding="utf-8") as f:
        f.write(json.dumps(rec, ensure_ascii=False) + "\n")


if __name__ == "__main__":  # smoke test — prints IDs + lengths, not the private text
    pub = {**PUBLIC_SENTENCES}
    cor = corpus_sentences()
    print(f"public: {len(pub)} | corpus: {len(cor)} | EP(cpu,non-ct)={providers(convtranspose=False)}")
    for k, v in cor.items():
        print(f"  {k}: {len(v)} chars")
