# Pattern skeletons

Each pattern: invariant skeleton, then worked examples. Voice varies per product (see voice-and-tone.md); the skeletons don't.

Contents: [Buttons & CTAs](#buttons--ctas) · [Links](#links) · [Errors](#error-messages) · [Empty states](#empty-states) · [Confirmations](#confirmation-dialogs) · [Success & notifications](#success-messages-toasts-notifications) · [Loading & progress](#loading--progress) · [Forms](#forms-labels-placeholders-helper-text-validation) · [Tooltips](#tooltips) · [Settings](#settings-labels--descriptions) · [Onboarding](#onboarding) · [Localization](#string-authoring-for-localization)

## Buttons & CTAs

Skeleton: `[Verb] [specific object]` — 1–3 words, the outcome, not the mechanism. Show the state the click *produces* (a playing player shows "Pause").

| Weak | Strong | Why |
|---|---|---|
| Submit | Create account | Names the outcome |
| OK (dismissal) | Close | "OK" implies the problem is OK |
| Get started | Take the style quiz | "Get started" fits any goal, so it informs none |
| Yes / No | Delete file / Keep file | Users act on the label alone, skipping body text |
| Learn more | See pricing details | Must stand alone out of context |
| + Add | + | Let the icon talk when it's unambiguous |

Fixed lexicon stays fixed: Back, Cancel, Done, Next, Skip — same word everywhere it appears. "Create" = new resource; "Add" = existing resource brought in; "Install" = software.

## Links

The 4 Ss (NN/g, eyetracking-backed): **Specific** (what's on the other side), **Sincere** (the label is the actual destination), **Substantial** (makes sense read alone — users read link text without surrounding copy), **Succinct** (but the first three win over brevity). Front-load: the first words carry the click decision.

## Error messages

Skeleton: `[What happened] + [why, only if it helps] + [how to fix]` — 12–18 words when possible, next to the source, preserved input, one message per detectable cause.

- ✗ "An error occurred." → ✓ "This file is too large (max 10 MB). Try compressing it."
- ✗ "Invalid email." → ✓ "This doesn't look like an email address. Check for typos?"
- ✗ "Field is blank." → ✓ "Enter your street address."
- ✗ "You entered the wrong password." → ✓ "Incorrect password." (passive is *correct* here — active voice would blame the user)
- ✗ "Error 0x80070005." → ✓ "Deckle can't access the microphone. Allow microphone access in Windows Settings > Privacy." (code goes below, as `Error code: ####`, only if actionable for support)

Guess the fix when you can: "Did you mean **gmail.com**?" State the requirement, not the violation: "Password must be at least 8 characters" > "Password too short". No humor — errors repeat, jokes stale.

Hierarchy: **prevent** (constrained controls, good defaults, auto-correction) > **tolerate** (accept flexible formats) > **recover** (a good message). Don't report states the user considers fine.

## Empty states

Three jobs, in order: state the status → teach → give a real pathway (an actual button, not advice).

- First use: "No projects yet. Create your first project to start collaborating." + [Create project]
- User-cleared: "Your inbox is clear. New messages will appear here." (reassure, don't push)
- Filtered/no results: "No records for this date range." + [Clear filters]
- Learning cue pattern: "Star your favorites, and you'll see them here."

Avoid: "It's lonely in here" (cliché), question-form openers ("Haven't connected a printer?" — pressure, condescension when repeated).

## Confirmation dialogs

Only when the outcome is unexpected or irreversible — habituation destroys overused confirmations; prefer undo where feasible.

Skeleton: heading = the outcome as a question with the count; body = details and the variable (item names — variables never go in headings, they truncate); buttons restate the verb; default focus on the safe option; no default at all for the truly destructive; high-risk → typed confirmation.

- ✗ "Are you sure?" [OK] [Cancel] → ✓ "Delete 3 recordings?" / "This can't be undone." [Delete] [Cancel]
- Simple action → specific verbs ("Delete all" / "Cancel"). Complex destructive → "Yes" / "No" forces reading the question.
- Result before action in instructions: "To restart Windows, select OK" — not "Select OK to restart Windows" (prevents reflex clicks).

## Success messages, toasts, notifications

- Success: past tense, specific, short, full-sentence punctuation: "Changes saved." "Link copied."
- Progression triad (Windows Admin Center pattern): in progress = verb + ellipsis ("Creating the volume 'Customer data'…"); success = "Successfully created the volume 'Customer data'."; failure = "Couldn't create the volume 'Customer data'." Include the object variable so the user knows which operation this is.
- Channel matches severity: toast/banner for passive info, modal only for must-resolve. **Never put an error in a toast** — it fades before it's seen.
- Every notification is an interruption costing real recovery time; justify it or batch it.

## Loading & progress

Thresholds (classic HCI, empirical): < 1 s — nothing. 2–10 s — spinner + what's happening ("Generating report…"). > 10 s — percent-done progress and, if possible, a time estimate. Ellipsis marks the in-progress state.

## Forms: labels, placeholders, helper text, validation

- **Label**: persistent, visible, above the field, readable out of context ("Billing phone", not "Phone" under a heading). Placeholder-as-label failed in every large-scale test — input erases the label, users can't verify or recover.
- **Placeholder**: format example only ("name@example.com"), never instructions, never required info.
- **Helper text**: states requirements *before* the error ("Use 8+ characters with a number."), outside the field, persistent.
- **Validation timing is copy too**: validate on leaving the field (or at full length for fixed-length fields); never mid-typing; clear the error on the keystroke that fixes it; confirm success positively.

## Tooltips

Supplementary only — the surface disappears, so nothing task-critical, no requirements, no instructions the user must retain. Never repeat the visible label; add something ("Visible only to your team."). Unlabeled icon → noun phrase ("Highlighting pen"); labeled-but-unclear control → imperative ("Find text in this file"). Shortcut in parentheses: "Print (Ctrl+P)". No terminal period unless multiple sentences.

## Settings labels & descriptions

- Label = the thing controlled, noun-first, no verb padding: "Startup sound", not "Enable startup sound" (the toggle already says on/off).
- Description = the consequence, one sentence, no period-ambiguity with the label above: "Play a sound when Deckle starts."
- First person marks user ownership in controls: "Remember my password", "Notify me when a transcript is ready".
- Same term as everywhere else in the product — settings pages are where terminology drift shows first.

## Onboarding

One concept per surface, dismissible, never blocking. Cap tips at ~4 per session. Teach at the moment of relevance, not upfront. "Show, then get out of the way" — offer just enough to start, then disappear.

## String authoring for localization

- One string = one whole sentence; punctuation inside the translatable unit. Never assemble sentences from fragments — word order, gender, and declension differ per language.
- Named or positional placeholders the translator can reorder: `Items in the cart: {count}` — never `"text " + var`.
- Plurals are authored as distinct forms (ICU plural categories), never "{n} item(s)".
- Don't reuse one string in two grammatical roles (a word valid as a label may be wrong as a button).
- Budget for expansion: strings ≤10 chars can grow +200–300% in translation; the tightest slots (buttons, tabs, headers) grow the most.
- Give translators context comments on every string with a placeholder.
