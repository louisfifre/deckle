"""Règles objectives qui ne demandent pas d'appel LLM.

  - ``wer``     : Word/Character Error Rate via jiwer
  - ``looping`` : détection de bouclage n-gram (pathologie Whisper)
  - ``leak``    : patterns d'hallucination + leak custom optionnels
"""
