---
description: Autocorrect observes via Raw Input + UIA and repairs after the word commits; no low-level hook, Enter is a reset.
type: adr
---

# ADR-0018 — Autocorrect observes Raw Input and repairs after the word commits

**Status** — proposed (2026-06-12)

## Context

Machine-wide autocorrect must see every keystroke and rewrite text in applications Deckle does not
control. Windows offers no machine-wide facility: the OS autocorrect reaches only TSF-aware controls,
and a custom TSF text service is in-proc COM C++ loaded into every GUI process — out of reach for a
managed v1. Two observation mechanisms remain. A low-level keyboard hook (`WH_KEYBOARD_LL`) is the
only one that can *suppress or retain* a keystroke — which is the only way to correct the last word
before an Enter that sends a chat message — but it adds latency to all system typing, and Windows
silently removes it after timeouts (the documented PowerToys Quick Accent failure class, the very
tool this workstream replaces). Raw Input (`RIDEV_INPUTSINK`) is observe-only: zero added latency, no
timeout, keeps seeing keystrokes when elevated windows have focus, and is already Deckle's proven
brick (`Deckle.Input.RawInputHost`).

## Decision

We will observe keystrokes via **Raw Input**, complemented by **targeted UIA reads** of the focused
element (password gate, control type), and repair a committed word by **`SendInput` Unicode
backspace+retype** after its boundary character lands. We will NOT install a low-level keyboard hook.

**Enter is a reset, never a correction trigger.** The word immediately preceding an Enter leaves
uncorrected, everywhere — an accepted v1 cost, since in-place repair after a send context has
dispatched the message is impossible by construction.

## Consequences

Easier: no impact on system-wide typing latency; no silent-removal failure mode; observation keeps
working when elevated windows hold focus; reuses the existing input thread pattern, injection interop
and UIA probe. Harder or impossible: correcting the final word of a chat message (espanso #2299 is
the same structural limit); injection into elevated windows stays UIPI-gated behind the opt-in
elevated task, same posture as the trackpad. Injection races with continued typing are bounded by
single-batch `SendInput` calls and abort-on-physical-input, not by key retention.

Re-evaluate (new ADR) if real usage shows the uncorrected word before Enter is the dominant miss: a
hook scoped to enrolled send-context apps could complement observation without replacing it.

## Options considered

- **A. WH_KEYBOARD_LL hook** — solves Enter by retaining the boundary key; rejected: pays permanent
  system-wide latency and the silent-removal risk for a case confined to send contexts.
- **B. TSF text service (TIP)** — the Microsoft-grade insertion path, no synthetic keystrokes;
  rejected for v1: in-proc COM C++ in every GUI process, HKLM registration, impractical from managed
  code. Remains the long-term high-fidelity option.
- **C. Raw Input + UIA reads + SendInput repair** — retained.
