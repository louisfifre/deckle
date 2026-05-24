"""Juge Gemini via le SDK ``google-genai`` (le nouveau, à ne pas
confondre avec ``google-generativeai`` qui est déprécié).

Gemini est le juge multimodal du bench : contrairement à Claude API qui
n'évalue qu'à partir du texte hypothèse + référence, Gemini reçoit le
WAV brut en entrée et peut juger directement contre le signal audio.
Cela règle le défaut méthodologique majeur du juge Claude (pas d'accès
au signal réel) — un axe comme ``fidelite_signal`` devient une mesure
contre le son entendu, pas une inférence par comparaison textuelle.

Architecture symétrique à ``claude.py`` :
  - SDK importé en lazy à l'instanciation pour qu'un bench qui n'utilise
    pas Gemini ne paie pas le coût d'import.
  - Réponse forcée en JSON strict via ``response_schema`` du SDK — plus
    robuste qu'un parsing post-hoc de markdown.
  - ``score_macro`` non implémenté (le mode macro attendra Gemini 3.5
    Pro quand il sortira ; en attendant, score_row suffit pour le POC).

Env requis :
  - ``GEMINI_API_KEY`` set. Clé à générer sur
    https://aistudio.google.com/apikey. Chargée via ``lib.env.load_dotenv``
    à l'entrée du bench.
"""

from __future__ import annotations

import json
import os
import re
import time
from pathlib import Path
from typing import Any

from ._base import Judge, JudgeScore


# Quota tier gratuit constaté empiriquement sur gemini-3.5-flash :
# 5 requêtes/minute. La doc Google ne publie plus de chiffres figés mais
# l'erreur 429 le confirme (``quotaValue: '5'``). 12 s = 60/5 c'est le
# fallback safe si l'API ne renvoie pas de retryDelay parseable.
_RATE_LIMIT_FALLBACK_DELAY_S = 12.0
_RATE_LIMIT_MARGIN_S = 1.0
_RATE_LIMIT_MAX_RETRIES = 3


# Modèle par défaut. Snapshot daté plutôt que ``gemini-flash-latest``
# pour reproductibilité bench. La famille 3.5 est la dernière disponible
# en GA via le SDK google-genai à date — voir la grille de pricing sur
# https://ai.google.dev/gemini-api/docs/pricing.
DEFAULT_ROW_MODEL = "gemini-3.5-flash"


# Schéma JSON Schema que Gemini doit respecter. Aligné sur les axes du
# juge Claude pour permettre la comparaison croisée, à la différence que
# ``fidelite_signal`` ici est mesuré contre l'audio écouté et non par
# inférence hyp/ref.
_JUDGE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "fidelite_signal":       {"type": "integer", "minimum": 0, "maximum": 100},
        "proprete":              {"type": "integer", "minimum": 0, "maximum": 100},
        "absence_hallucination": {"type": "integer", "minimum": 0, "maximum": 100},
        "regime_respecte":       {"type": "integer", "minimum": 0, "maximum": 100},
        "whisper_ref_suspecte":  {"type": "boolean"},
        "verdict":               {"type": "string"},
    },
    "required": [
        "fidelite_signal",
        "proprete",
        "absence_hallucination",
        "regime_respecte",
        "whisper_ref_suspecte",
        "verdict",
    ],
    "propertyOrdering": [
        "fidelite_signal",
        "proprete",
        "absence_hallucination",
        "regime_respecte",
        "whisper_ref_suspecte",
        "verdict",
    ],
}


class GeminiJudge(Judge):
    """Juge Gemini multimodal."""

    name = "gemini"
    label = "Gemini API (Google AI Studio)"

    def __init__(
        self,
        *,
        row_system_prompt:   str,
        row_model:           str = DEFAULT_ROW_MODEL,
        max_audio_inline_mb: float = 18.0,
        max_tokens_row:      int = 1024,
        temperature:         float = 0.0,
    ) -> None:
        try:
            from google import genai
            from google.genai import types
        except ImportError as exc:
            raise RuntimeError(
                "Package 'google-genai' requis pour le juge Gemini. "
                "Install dans le venv : pip install google-genai"
            ) from exc

        if not os.environ.get("GEMINI_API_KEY"):
            raise RuntimeError(
                "GEMINI_API_KEY non défini.\n"
                "  Créer un fichier benchmark/.env avec :\n"
                "    GEMINI_API_KEY=AIza...\n"
                "  Clé à générer sur https://aistudio.google.com/apikey\n"
                "  puis appeler lib.env.load_dotenv() avant d'instancier GeminiJudge."
            )

        self._client = genai.Client(api_key=os.environ["GEMINI_API_KEY"])
        self._types = types
        self.row_system_prompt = row_system_prompt
        self.row_model = row_model
        # 18 MB plutôt que le hard cap 20 MB Google : marge pour le
        # texte qui s'ajoute à l'audio dans la même requête. Au-dessus
        # de cette taille on lève — le code Files API n'est pas câblé
        # (corpus actuel : 252 s × 16 kHz mono 16 bit ≈ 8 MB max).
        self.max_audio_inline_mb = max_audio_inline_mb
        self.max_tokens_row = max_tokens_row
        self.temperature = temperature

    # ── Per-row ────────────────────────────────────────────────────────

    def score_row(
        self,
        *,
        hypothesis:    str,
        reference:     str | None,
        regime_name:   str,
        regime_label:  str,
        source_name:   str,
        audio_path:    Path | None = None,
    ) -> JudgeScore:
        t0 = time.perf_counter()
        try:
            parts = self._build_parts(
                hypothesis=hypothesis,
                reference=reference,
                regime_name=regime_name,
                regime_label=regime_label,
                source_name=source_name,
                audio_path=audio_path,
            )
            response = self._call_with_retry(parts)
            raw = response.text or ""
        except Exception as exc:
            return JudgeScore(
                axes={}, verdict="", raw_response=f"[ERROR] {type(exc).__name__}: {exc}",
                parse_ok=False, elapsed_s=time.perf_counter() - t0,
                model=self.row_model,
            )

        elapsed = time.perf_counter() - t0
        parsed = _parse_json(raw)
        return JudgeScore(
            axes=parsed["axes"],
            verdict=parsed["verdict"],
            raw_response=raw,
            parse_ok=parsed["parse_ok"],
            elapsed_s=elapsed,
            model=self.row_model,
            extras={"usage": _extract_usage(response)},
        )

    # ── Macro (non câblé) ──────────────────────────────────────────────

    def score_macro(self, *, run_summary, examples):
        # Le mode macro attend Gemini 3.5 Pro (rollout juin 2026) — en
        # attendant on reste sur le per-row, conforme à la doctrine
        # _base.Judge qui prévoit explicitement cette absence.
        raise NotImplementedError(
            "GeminiJudge.score_macro pas câblé. À implémenter quand "
            "Gemini 3.5 Pro sera disponible, ou utiliser un juge gros "
            "alternatif pour le macro."
        )

    # ── Internals ──────────────────────────────────────────────────────

    def _call_with_retry(self, parts: list[Any]) -> Any:
        """Appel ``generate_content`` avec retry sur 429 RESOURCE_EXHAUSTED.

        Le SDK ``google-genai`` ne respecte pas tout seul le ``retryDelay``
        renvoyé dans le payload d'erreur. On le parse et on sleep d'autant
        — sans ça les rows courtes burn le quota free tier (5 RPM) en
        moins d'une minute et la moitié des juges remontent en erreur.

        Stratégie : retryDelay extrait du payload + 1 s de marge. Si on ne
        sait pas le parser, fallback 12 s (60/5 = 1 call max par 12 s).
        Plafond de 3 tentatives — au-delà c'est probablement un problème
        de plan ou de clé et on laisse remonter.
        """
        last_exc: Exception | None = None
        for attempt in range(_RATE_LIMIT_MAX_RETRIES):
            try:
                return self._client.models.generate_content(
                    model=self.row_model,
                    contents=parts,
                    config=self._types.GenerateContentConfig(
                        system_instruction=self.row_system_prompt,
                        response_mime_type="application/json",
                        response_schema=_JUDGE_SCHEMA,
                        temperature=self.temperature,
                        max_output_tokens=self.max_tokens_row,
                    ),
                )
            except Exception as exc:
                last_exc = exc
                if not _is_rate_limit_error(exc):
                    raise
                if attempt == _RATE_LIMIT_MAX_RETRIES - 1:
                    raise
                delay = _extract_retry_delay(exc) or _RATE_LIMIT_FALLBACK_DELAY_S
                time.sleep(delay + _RATE_LIMIT_MARGIN_S)
        # Inatteignable : la boucle sort soit par return soit par raise.
        # Mypy/Pyright voudraient un raise explicite ici.
        raise last_exc if last_exc is not None else RuntimeError("retry loop exited unexpectedly")

    def _build_parts(
        self,
        *,
        hypothesis:   str,
        reference:    str | None,
        regime_name:  str,
        regime_label: str,
        source_name:  str,
        audio_path:   Path | None,
    ) -> list[Any]:
        """Construit la liste ``contents`` envoyée à Gemini. L'ordre
        compte (audio d'abord, texte ensuite) — recommandé par la doc
        Gemini pour que le modèle écoute avant de lire les jugements à
        rendre."""
        parts: list[Any] = []

        if audio_path is not None:
            if not audio_path.exists():
                raise FileNotFoundError(f"audio_path inexistant : {audio_path}")
            size_mb = audio_path.stat().st_size / (1024.0 * 1024.0)
            if size_mb > self.max_audio_inline_mb:
                raise RuntimeError(
                    f"Audio {audio_path.name} pèse {size_mb:.1f} MB > "
                    f"{self.max_audio_inline_mb} MB. Files API non câblée "
                    f"pour ce juge — sample trop long pour inline_data."
                )
            data = audio_path.read_bytes()
            mime = _mime_from_ext(audio_path.suffix)
            parts.append(self._types.Part.from_bytes(data=data, mime_type=mime))

        text_msg = _build_text_message_row(
            hypothesis=hypothesis,
            reference=reference,
            regime_name=regime_name,
            regime_label=regime_label,
            source_name=source_name,
            has_audio=audio_path is not None,
        )
        parts.append(text_msg)
        return parts


# ── Helpers libres ────────────────────────────────────────────────────

def _is_rate_limit_error(exc: Exception) -> bool:
    """``True`` si l'exception est un 429 RESOURCE_EXHAUSTED de Gemini.

    Le SDK google-genai expose le code HTTP via différents attributs selon
    la version — on couvre les principaux et on fallback sur le texte de
    l'exception. Un faux positif serait peu coûteux (sleep inutile) mais
    pas une régression silencieuse."""
    code = getattr(exc, "code", None) or getattr(exc, "status_code", None)
    if code == 429:
        return True
    s = str(exc)
    return "429" in s and ("RESOURCE_EXHAUSTED" in s or "rate limit" in s.lower())


_RETRY_DELAY_PATTERNS = (
    re.compile(r"'retryDelay':\s*'(\d+(?:\.\d+)?)s'"),
    re.compile(r'"retryDelay":\s*"(\d+(?:\.\d+)?)s"'),
    re.compile(r"retry in (\d+(?:\.\d+)?)\s*s"),
)


def _extract_retry_delay(exc: Exception) -> float | None:
    """Extrait le ``retryDelay`` (en secondes) d'un 429 Gemini.

    1. Essaie d'abord la voie structurée : ``exc.details`` est parfois une
       liste de dicts incluant un bloc ``google.rpc.RetryInfo``.
    2. Sinon parse la repr str() de l'exception qui contient typiquement
       ``'retryDelay': '32s'``.
    Retourne ``None`` si rien d'exploitable — le caller fallback alors
    sur une valeur sûre."""
    details = getattr(exc, "details", None)
    if isinstance(details, list):
        for d in details:
            if isinstance(d, dict) and d.get("@type", "").endswith("RetryInfo"):
                rd = str(d.get("retryDelay", ""))
                m = re.match(r"^(\d+(?:\.\d+)?)s$", rd)
                if m:
                    return float(m.group(1))
    s = str(exc)
    for pat in _RETRY_DELAY_PATTERNS:
        m = pat.search(s)
        if m:
            return float(m.group(1))
    return None


def _mime_from_ext(ext: str) -> str:
    """MIME type à passer à Gemini ``Part.from_bytes`` selon l'extension
    du fichier audio. Defaulte à audio/wav vu le profil du corpus."""
    return {
        ".wav":  "audio/wav",
        ".mp3":  "audio/mpeg",
        ".m4a":  "audio/mp4",
        ".ogg":  "audio/ogg",
        ".flac": "audio/flac",
    }.get(ext.lower(), "audio/wav")


def _build_text_message_row(
    *,
    hypothesis:   str,
    reference:    str | None,
    regime_name:  str,
    regime_label: str,
    source_name:  str,
    has_audio:    bool,
) -> str:
    """Construit le message texte qui accompagne l'audio. Format conçu
    pour rester lisible quand on inspecte le payload côté Google AI
    Studio. Le system prompt côté ``response_schema`` impose déjà le
    JSON ; on n'a pas besoin de le rappeler ici."""
    parts = [
        f"Source       : {source_name}",
        f"Régime       : {regime_name} — {regime_label}",
    ]
    if has_audio:
        parts.append("Audio        : fourni en entrée (écoute le signal).")
    if reference is not None:
        parts.append(
            f"\nRÉFÉRENCE (Whisper large-v3, peut contenir hallucinations) :\n"
            f"<<<REF>>>\n{reference.strip()}\n<<<END>>>"
        )
    parts.append(
        f"\nHYPOTHÈSE à évaluer :\n"
        f"<<<HYP>>>\n{hypothesis.strip()}\n<<<END>>>"
    )
    return "\n".join(parts)


def _parse_json(raw: str) -> dict[str, Any]:
    """Parse la réponse JSON. Avec ``response_schema`` strict côté
    Gemini, ``raw`` est toujours du JSON valide direct — pas besoin du
    récup-bricolage que ``claude.py`` fait pour extraire un bloc
    markdown.

    Convention identique à claude.py : tout ce qui n'est pas un champ
    verdict est traité comme un axe — laisse de la marge si on ajoute
    des axes au schéma sans toucher au code."""
    if not raw or not raw.strip():
        return {"axes": {}, "verdict": "", "parse_ok": False}
    try:
        obj = json.loads(raw)
    except json.JSONDecodeError:
        return {"axes": {}, "verdict": "", "parse_ok": False}
    if not isinstance(obj, dict):
        return {"axes": {}, "verdict": "", "parse_ok": False}
    verdict_keys = {"verdict", "verdict_court", "commentaire", "comment"}
    axes = {k: v for k, v in obj.items() if k not in verdict_keys}
    verdict = ""
    for k in ("verdict", "verdict_court", "commentaire", "comment"):
        if k in obj:
            verdict = str(obj[k])
            break
    return {"axes": axes, "verdict": verdict, "parse_ok": True}


def _extract_usage(response: Any) -> dict[str, int]:
    """Extrait le compte de tokens d'une réponse Gemini. Optionnel,
    silencieux en cas d'API qui change."""
    try:
        meta = response.usage_metadata
        return {
            "input_tokens":  int(getattr(meta, "prompt_token_count", 0) or 0),
            "output_tokens": int(getattr(meta, "candidates_token_count", 0) or 0),
            "total_tokens":  int(getattr(meta, "total_token_count", 0) or 0),
        }
    except Exception:
        return {}
