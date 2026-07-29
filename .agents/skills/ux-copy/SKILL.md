---
name: ux-copy
description: Write or review interface wording — CTAs, errors, empty states, confirmations, tooltips, notifications, loading states, forms, settings labels, onboarding. Use when drafting or critiquing any UI string; for research, flows, or information architecture use ux-designer instead.
type: skill
---

# UX copy

## Intent

Words are the interface: users act on the label alone, skipping everything around it. The craft is making each string carry its action out of context, in the fewest words that stay human.

## How

Start from the moment, not the string: what just happened, and what the user feels. Tone follows the user's state, never a fixed setting — the higher the stress, the plainer the language; humor only when the user just succeeded, and even then dry. Voice stays constant per product; tone moves. Then the platform: casing, punctuation, button order are the host's conventions, not choices to make.

Users scan, they don't read. Front-load the word that carries the decision, one idea per sentence, plain short words over their formal twins. A sentence past twenty words is two sentences. Write to the user in the active present — second person, contractions, numerals.

Buttons name the outcome — a verb and its object, specific enough to act on alone. A label that fits any goal informs none.

An error states what happened and how to fix it — the app owns the fault, never the user. No blame words, no raw codes, no humor: errors repeat, jokes stale. The best error is the one a constrained control prevented; the second best states the requirement, not the violation. Please and sorry are rationed: please for the inconvenient, sorry for the serious — and always with the fix.

A label is persistent and readable out of context; a placeholder is a format hint, never the label. Requirements live in helper text before the error, never in a tooltip — nothing load-bearing survives in a surface that disappears.

Interruption is a budget: the lightest surface that does the job — inline, then toast, then banner, then modal. An error about what is on screen never rides a surface that fades; a background operation reports its failure in a persistent notification, its only channel. A destructive confirmation names the consequence and the count, buttons restate the verb, and the safe option is the default — Enter never destroys.

A string is authored whole: one full sentence per string, reorderable placeholders, real plural forms — never fragments glued together. The shortest strings grow the most in translation.

Before shipping, hold every string against every rule above — and read it aloud: if no human would say it, rewrite it. The checks most often failed:

- the label alone says what happens
- the error gives the fix, without blame
- nothing critical hides in a tooltip or placeholder
- same term for the same thing everywhere
- casing and punctuation match the platform

## Pointers

- Writing or reviewing a surface → [references/patterns.md](references/patterns.md), its skeleton and weak → strong examples.
- Setting or checking tone → [references/voice-and-tone.md](references/voice-and-tone.md), the voice chart and tone by user state.
- The app ships on Windows → [references/windows-fluent.md](references/windows-fluent.md), the Microsoft register, applied as-is.
