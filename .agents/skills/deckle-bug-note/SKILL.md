---
name: deckle-bug-note
description: Use when a confirmed bug has its cause established and its workstream is still open, to record what the bug teaches beside the tests that own the behavior.
---

## Why

A bug that is understood and then forgotten comes back under another name. Its note lets a later test or review recognize it.

## Statement

Write one note beside the tests that own the behavior, in that folder's `<Subject>BugNotes.md`, under a heading that says what went wrong. The note records the trigger, the observed symptom, the cause, the violated invariant and a recurrence cue. It names the test that guards the behavior, or says that none does yet and why. Each field holds what a later reader needs to recognize the bug coming back and to trust its cause.

State facts plainly and mark what is uncertain, so a reader can tell what was observed from what was inferred. The note carries no secret and no personal data.

This skill writes the note. The fix and its regression test belong to the workstream that fixes the bug, as `deckle-testing` describes.
