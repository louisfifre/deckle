---
name: ux-copy
description: Write or review interface wording — CTAs, errors, empty states, confirmations, tooltips, notifications, loading states, forms, settings labels, onboarding. Use when drafting or critiquing any UI string; for research, flows, or information architecture use ux-designer instead.
argument-hint: "<context or copy to review>"
---

# /ux-copy

Write and review interface microcopy. This skill's value is in three things generic instinct gets wrong: evidence-based defaults, pattern skeletons with worked examples, and a review gate. Load references on demand, only the ones the task touches:

- [references/patterns.md](references/patterns.md) — per-context skeletons with before → after examples
- [references/voice-and-tone.md](references/voice-and-tone.md) — voice chart, tone dimensions, tone by user state, humor gating
- [references/windows-fluent.md](references/windows-fluent.md) — Microsoft/Windows platform mechanics (casing, punctuation, error anatomy, dialogs)
- [references/evidence.md](references/evidence.md) — the research behind the rules, with sources and numbers

## Intake

Infer from context when possible; ask only for what is missing and load-bearing.

1. **Surface and moment** — which control, which screen, what just happened, what happens next.
2. **User state** — goal, and emotional state (frustrated mid-error? exploring? just succeeded?). Tone derives from this, not from a fixed setting.
3. **Platform** — decides casing, punctuation, button order, dismissal labels. Windows → read windows-fluent.md.
4. **Voice** — existing product voice and terminology. If none exists, propose one via the voice chart in voice-and-tone.md.
5. **Constraints** — character limits, localization, sibling strings the new one must sit beside.

## Defaults

These override generic instincts. Deviate only for a stated reason.

- **Sentence case everywhere** — buttons, labels, titles, headings. Title case only where the platform mandates it (iOS/macOS).
- **No terminal period** on buttons, labels, headings, single-sentence tooltips. Periods on full sentences in bodies, errors, toasts. Question marks where a question is posed. No exclamation points outside genuine celebration.
- **Buttons are outcome verbs**, 1–3 words: "Create account", not "Submit"; "Delete file" / "Keep file", not "Yes" / "No". Generic dismissal is "Close", never "OK". Ban "Get started" and bare "Learn more" — users act on the label alone, so it must stand alone.
- **Errors = what happened + how to fix it** (+ why, only if it helps). Never blame: no "invalid", "illegal", "failed", "fatal", "forbidden". No raw codes in the message body ("Error code: ####" below, only if actionable). The app owns the fault. The best error is the one prevented by a constrained control or a better default.
- **"Please" and "sorry" are rationed.** "Please" only when asking something inconvenient or when the product is at fault. "Sorry" only for serious failures (data loss, cannot continue) — and then paired with an explanation and a fix, never bare.
- **Humor is state-gated**: off unless the user just succeeded, and never in errors, payments, or destructive flows. Trust beats friendliness — when in doubt, be plain.
- **Second person, active voice, present tense, contractions, numerals** ("3 items"), Oxford comma. Cut "you can", "there is/are". Front-load the load-bearing word — users scan, they don't read.
- **Sentences 15–20 words**, one idea each, no nested clauses. Plain words: "use" not "utilize", "turn on" not "enable", "about" not "approximately".
- **Labels are persistent and out-of-context readable**; a placeholder is never the label, only a format hint. Requirements live in helper text before the error, never in a tooltip.
- **Tooltips are never load-bearing** — supplementary only; nothing task-critical may live in a surface that disappears.
- **Interruption is a budget**: prefer the lightest surface (inline > toast > banner > modal); modals only for severe, must-resolve cases. Toasts are unfit for errors.
- **Strings survive translation**: whole sentences with named placeholders, never concatenated fragments; distinct plural forms, never "(s)"; expect short strings to grow +200–300%.

## Workflow — writing

1. Intake, then classify the string's job: **inform** (titles, tooltips, status), **influence** (CTAs, onboarding), or **interact** (buttons, labels, nav).
2. Read the matching skeleton in patterns.md; check platform mechanics if the platform is known.
3. Draft 2–3 options at the target tone (voice-and-tone.md), front-loaded, within constraints.
4. Run each through the pre-ship gate below; discard or fix failures.
5. Deliver the recommended option first with a one-line rationale, alternatives after. Flag any new term the copy introduces into the product's vocabulary.

## Workflow — reviewing

1. Inventory the strings in scope; classify each by job (inform / influence / interact).
2. Check each against the pre-ship gate and its pattern skeleton.
3. Check across strings: same term for the same thing, parallel structure in parallel items, consistent casing and punctuation.
4. Report per string — verdict, what fails and why, proposed rewrite. Lead with the fixes that change user behavior; casing nits last.

## Pre-ship gate

Binary — a string failing any item goes back.

- [ ] Understandable out of context — the label alone says what happens
- [ ] Action labels are specific outcome verbs; no Submit / OK / Get started / Learn more
- [ ] Error states the problem and the fix, without blame words or raw codes
- [ ] Destructive confirmation names the consequence and the count ("Delete 4 items? This can't be undone."); buttons restate the verb; safe option holds default focus
- [ ] Casing and punctuation match the platform
- [ ] Nothing load-bearing in a tooltip or placeholder
- [ ] Terminology consistent with existing strings
- [ ] Reads naturally when spoken aloud
- [ ] Translation-safe: full sentences, reorderable placeholders, real plural forms
