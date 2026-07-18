# Voice and tone

The shared model across Mailchimp, Atlassian, Salesforce, Apple, Material: **voice is constant, tone varies** — with the user's emotional state and the stakes of the moment, never at random.

## What the evidence says about tone

NN/g's quantitative studies (see evidence.md) are the only empirical spine here:

- Tone lives on four independent spectra: **funny ↔ serious**, **formal ↔ casual**, **respectful ↔ irreverent**, **enthusiastic ↔ matter-of-fact**. Position a product anywhere on each axis; extremes are not required.
- Casual, conversational, moderately enthusiastic profiles rated best overall.
- **Trustworthiness explains 52% of desirability; friendliness adds ~8%.** Playfulness can *lower* trust in serious domains. When personality and clarity conflict, clarity wins — every major style guide agrees.

## Tone by user state

The higher the user's stress, the plainer the language.

| User state | Tone | Never |
|---|---|---|
| Frustrated (error, blocked) | Empathetic, directive — the fix first | Humor, exclamation, cheerfulness |
| Confused (new, lost) | Patient, explanatory, literal | Jargon, irony |
| Cautious (payment, destructive, privacy) | Serious, transparent, precise | Playfulness, vagueness |
| Confident (power user mid-flow) | Efficient, direct, minimal | Hand-holding, "you can…" |
| Successful (just completed) | Positive, brief; the one slot where delight is allowed | Over-celebration |

Humor rule: off unless the user just succeeded — and even then dry and optional. Forced humor is worse than none; jokes in errors stale on repetition.

## Apology policy

Reconciling Microsoft's rationing with the apology research (both in evidence.md): apologize **rarely** — only when the product is genuinely at fault or harm occurred (data loss, cannot continue). When you do apologize, an **explanatory** apology (what went wrong + the fix) outperforms both rote "Sorry!" and pure empathy. Never apologize for external conditions (network down, third-party failure) — there, state the fact and the way back: "You're not connected. Let's get you back online."

## Voice chart

The seam that keeps patterns product-agnostic: the skeleton is invariant, the voice chart renders it. Podmajersky's six dimensions — fill one row per product principle:

| Product principle | Vocabulary | Verbosity | Grammar | Punctuation & caps | Ideal example |
|---|---|---|---|---|---|
| e.g. "Calm and capable" | everyday words, no superlatives | minimal — cut until it breaks | verb-first imperatives | sentence case, no ! | "Transcript ready. Press Ctrl+V to paste." |

Sharpen it with **target words** (the tone you write toward, e.g. *plain, assured, warm*) and **anti-tone words** (what you must never sound like, e.g. *pedantic, chirpy, corporate*). The anti-list catches more drift than the target list.

## One pattern, three voices

Same error skeleton (what happened + fix), rendered through different voice charts — the pattern holds, the voice moves:

- **Plain utility**: "Couldn't save the recording. Check that the output folder still exists."
- **Warm consumer**: "We couldn't save your recording — your audio is safe. Check that the output folder still exists."
- **Terse pro tool**: "Save failed: output folder not found. Choose another folder."

## Person

- **You/your** — default; often implied and omitted ("Store files online" not "You can store files online").
- **I/my/me** — only to voice the user's ownership in controls: "Remember my password", "I agree to the terms".
- **We/us** — minimized; reserved for accepting fault ("We couldn't upload the picture") and for privacy/security statements where the maker must be the visible speaker. Corporate "we" everywhere else reads as a looming presence.
