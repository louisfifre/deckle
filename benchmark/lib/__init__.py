"""Briques réutilisables pour les benches sous ``benches/``.

Sous-packages :
  - ``corpus``   : lecture des corpora curated sous ``corpora/<slug>/``
  - ``sources``  : drivers de transcription (Whisper, Voxtral, futurs)
  - ``judges``   : juges de qualité (Claude API, Ollama legacy)
  - ``metrics``  : règles objectives (WER, looping, leak patterns)
  - ``monitor``  : observabilité ressources (script PowerShell GPU/RAM)
"""
