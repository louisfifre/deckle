"""Load a benchmark ``.env`` file without a python-dotenv dependency.

One ``KEY=value`` per line; ``#`` lines and blank lines are skipped, and one
level of matching single or double quotes is stripped from the value.
A variable already set in the environment wins over the file unless
``override`` is true, so a key exported in the shell takes precedence.
"""

from __future__ import annotations

import os
from pathlib import Path


BENCHMARK_DIR = Path(__file__).resolve().parent.parent
DEFAULT_ENV_PATH = BENCHMARK_DIR / ".env"


def load_dotenv(path: Path | None = None, *, override: bool = False) -> dict[str, str]:
    """Load ``path`` (default ``benchmark/.env``) into ``os.environ`` and
    return the pairs read. A missing file is not an error: the key may
    already be exported in the shell."""
    p = path or DEFAULT_ENV_PATH
    if not p.exists():
        return {}

    pairs: dict[str, str] = {}
    for raw_line in p.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
            value = value[1:-1]
        pairs[key] = value
        if override or key not in os.environ:
            os.environ[key] = value
    return pairs
