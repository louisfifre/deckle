"""Shared helpers for benchmark workspaces.

This package is intentionally domain-neutral. Put only infrastructure here:
path resolution, environment loading, event logging, and resource monitoring.
Domain-specific corpora, sources, judges, metrics, and prompts belong in the
workspace that owns them, for example ``benchmark/asr/lib``.
"""
