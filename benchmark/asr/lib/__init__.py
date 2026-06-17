"""ASR-specific reusable blocks for the speech benchmark.

Sous-packages :
  - ``corpus``   : lecture des corpora curated sous ``corpora/<slug>/``
  - ``sources``  : drivers de transcription (Whisper, Voxtral, futurs)
  - ``judges``   : juges de qualité (Claude API, Ollama legacy)
  - ``metrics``  : règles objectives (WER, looping, leak patterns)

Transversal helpers live in ``benchmark/lib`` and are imported as ``lib.*``.
"""
