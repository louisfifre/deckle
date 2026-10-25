---
description: Every event's message template carries the shape its level calls for, a sentence, pairs, or a sentence then pairs, and a doctrine test holds each provider to it.
---

# ADR-0007 — Write a message's shape per level in the template

**Status** — accepted 2026-10-23

## Context

Deckle's 38 providers declare about 1160 events, each with a message template that mixed a sentence and `name={n}` values in one line. The log window showed them as one line, so a `Warning` read like a `Verbose` and a milestone carried values nobody read. The person reading the log wanted each level to read for what it is: `Informational` tells the steps, `Verbose` carries the detail, `Warning` and `Error` say what went wrong and then show the values.

The values already travel in the payload, where a filter or a tool reads them. The question was where the shape lives: in the template, or in code that renders the values line from the payload for every event.

## Decision

Each template MUST carry the shape of its level. At `Informational` the message is a sentence alone, with no separator and no value. At `Verbose` it is `name={n}` pairs only, separated by ` | `, with no sentence. At `Warning` and above it is a sentence, a line break, then the pairs; a sentence alone when the event has no payload. The payload keeps the values as before.

A doctrine test in `Deckle.TestSupport` MUST read every template by reflection and hold each provider to the shape. Each suite names, one by one with its reason, the events it still lets through, and that list MUST only shrink: an exception whose event has become conforming fails the test. The sense of a `Warning` or `Error` sentence, that it names its cause, is read by a person, not by the test.

## Consequences

A new event must conform or be listed with a reason, so the shape holds without anyone watching. A `Warning` or `Error` message carries a line break in the JSONL `message` field, escaped as the format requires; readers that assumed one line follow.

On screen a `Verbose` row shows pairs without a sentence, and an `Informational` row shows a sentence without its values; the values stay in the payload and in the file. Showing the event name beside a `Verbose` row is open.

The first pass over the templates was a script per level and a hand reading of the `Warning` and `Error` sentences, 344 of them, more than the 84 the review had counted.

## Options considered

- **A. Common code renders the values line from the payload** — every event gets the two-line form for free, but the template no longer says what the reader sees, and a test cannot check a shape that exists only at render time.
- **B. The shape written in the template itself (retained)** — one pass over every template and a test that holds the shape, for a template that reads as it renders.
