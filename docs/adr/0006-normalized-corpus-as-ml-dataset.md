---
name: adr-0006-normalized-corpus-as-ml-dataset
description: "Records the corpus redesign into a normalized ML dataset: separate ASR and rewrite JSONL events, flat audio deduplicated by transcription_id, bucketed by length tier and by rewrite profile. Read before touching corpus emission, the JSONL schema, or the on-disk telemetry layout."
type: adr
---

# ADR-0006 — Normalized corpus as ML dataset

**Status** — accepted 2026-05-23

## Context

The transcription corpus lived its first year as a recording journal parameterized by the LLM rewrite profile that followed it. The JSONL line and the WAV shared a `<profile-slug>-<profile-id>` slug, and the emitter block was gated by `if (CorpusEnabled && profile is not null)`. Two problems followed.

First — when the user transcribes without a resolved rewrite profile (LLM off, no profile bound to the hotkeys, no auto-rule matching), no trace is produced at all, even when `CorpusEnabled` and `RecordAudioCorpus` are both `true`. The originating bug of this work — 0 WAV ever written under `%LOCALAPPDATA%\Deckle\telemetry\` despite a long-active `RecordAudioCorpus` — comes from there.

Second — bucketing by rewrite profile couples the audio dataset to the prompt lifecycle. A profile rename changes the folder slug; a deletion orphans past analyses; a prompt variation without a rename hides that the sub-dataset is no longer homogeneous. The leftover legacy folders on disk witness it: the chosen structure never produced the dataset it promised.

The real need the corpus answers — an ML dataset to calibrate the ASR pipeline, compare Whisper models, measure each rewrite profile's quality over time, and prepare a second ASR backend (cf. ADR-0005) — requires an architecture that separates observation of the two independent pipeline layers (raw transcription / LLM rewrite) and deduplicates the audio per invocation, not per profile.

## Options considered

- **A. Keep the schema, drop only the `profile is not null` gate.** Fixes the visible bug, but the WAV↔profile coupling remains, ASR-only analyses land in a catch-all bucket, and second-backend preparation does not advance. A local bandage.
- **B. One enriched corpus event aggregating ASR + rewrite with a nullable `rewrite_profile_id`.** Elegant on the surface, but impossible to avoid repeating the ASR text when exploring several rewrite profiles on the same entry, a JSONL schema half-empty when rewrite is absent, no clean per-layer separation.
- **C. Two separate events and a flat audio deduplicated by `transcription_id`.** `CorpusAsrRecorded` captures the ASR output, `CorpusRewriteRecorded` the LLM rewrite. When a rewrite runs, both events are emitted with the same `transcription_id`. The audio lives once per transcription in a flat `audio/` folder, referenced by basename from each JSONL line. ASR output is bucketed by length tier, rewrite output by profile.

## Decision

Option C. The corpus becomes a normalized ML dataset along three orthogonal axes: one audio file per transcription (gated by `RecordAudioCorpus`), one ASR JSONL line per transcription bucketed by inference layer and length tier (gated by `CorpusEnabled`), one rewrite JSONL line per transcription when a profile runs, bucketed by profile (same gate).

The target layout under `<UserDataRoot>/telemetry/` is `audio/<transcription_id>.wav`, `corpus/raw/<tier>/corpus.jsonl` for raw ASR, `corpus/rewrite-<sluggified-name>-<profile-id>/corpus.jsonl` for rewrite outputs (and `corpus/<engine-instruction>/<tier>/` reserved for a future instruction-named ASR mode). The `transcription_id` is a short Guid (`N` format) generated once per pipeline invocation and carried by every JSONL line and the WAV name; combined with the process-scope `SessionId`, it gives a stable join. The five length tiers are computed on `word_count` with thresholds frozen in code (`very-short` 0–30, `short` 30–200, `medium` 200–1000, `long` 1000–3000, `very-long` >3000). Rewrite outputs are deliberately not tier-bucketed — the meaningful property of a rewrite bucket is the profile that produced the text, not its length.

## Consequences

Easier: exploiting the corpus as an ASR dataset without de-duplicating WAVs at analysis time; comparing Whisper against a future backend on the same raw corpus; measuring a rewrite profile's quality on identical ASR sequences (join by `transcription_id`); producing tier-stratified analyses without post-hoc grouping.

Harder: the boot bootstrap hosts a routed JSONL listener in addition to the flat ones — one more class to maintain. JSONL paths contain components derived from the rewrite profile name, so sanitation MUST be applied systematically on the producer side. Keeping the `transcription_id` coherent across the two emissions is a local discipline, but a single pipeline is concerned.

Impossible: recovering a WAV↔profile join for legacy artifacts generated before this redesign — they have no `transcription_id`, so no honest join exists. The legacy folders stay on disk as-is, no content migration; the user may delete them manually to reclaim space. Better to ignore than to invent a false join.
