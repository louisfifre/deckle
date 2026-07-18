# Patterns

One skeleton per surface, invariant; the voice that renders it lives in voice-and-tone.md.

## Buttons

`[Verb] [object]` — the outcome, not the mechanism; show the state the click produces (a playing player offers Pause).

| Weak | Strong |
|---|---|
| Submit | Create account |
| OK | Close |
| Yes / No | Delete file / Keep file |
| Get started | Take the style quiz |
| Learn more | See pricing details |

The fixed lexicon stays fixed — Back, Cancel, Done, Next, Skip — the same word everywhere it appears. Create makes a new resource; Add brings in an existing one; Install is for software.

## Links

Front-load: the first words carry the click. A link label is specific (what is on the other side), sincere (the label is the actual destination), and substantial (it reads alone — users read link text without the copy around it). Brevity comes last of the four.

## Errors

`[What happened] + [why, only if it helps] + [how to fix]` — next to its source, in words rather than color or an icon alone, input preserved, one message per detectable cause.

- "An error occurred." → "This file is too large (max 10 MB). Try compressing it."
- "Invalid email." → "This doesn't look like an email address. Check for typos."
- "Password too short." → "Password must be at least 8 characters."
- "You entered the wrong password." → "Incorrect password." — name the state, not the actor: a sentence with the user as subject would blame.
- Guess the fix when you can — "Did you mean gmail.com?" — except where the hint would leak what a secret should look like.

Prevent, then tolerate, then recover: a constrained control first, a flexible format accepted next, the message as last resort. A message earns its place only for a state the user would call a problem.

## Empty states

Status, then teaching, then a real pathway — the button that fills the surface or, when the filling action lives outside it, the one instruction that triggers it. Open with the status as a plain statement; it stays plain on the hundredth visit.

- First use: "No projects yet. Create your first project to start collaborating." + [Create project]
- User-cleared: "Your inbox is clear. New messages will appear here." — reassure, don't push.
- No results: "No records for this date range." + [Clear filters]

## Confirmations

Only for the unexpected or irreversible — overuse breeds automatic dismissal, and undo beats asking. The heading states the outcome, as a question or a plain statement, count included; names — files, apps, accounts — go in the body, where truncation can't eat them. The safe button is the default and holds focus — Enter never commits the destructive verb; typed confirmation for the highest stakes.

- "Are you sure?" [OK] [Cancel] → "Delete 3 recordings?" / "This can't be undone." [Delete] [Cancel]
- Result before action: "To restart now, select Restart" — the consequence meets the reader before the button does.

## Success and notifications

Past tense, specific, short: "Changes saved." "Link copied." An operation carries its object through its whole life — "Creating the volume 'Customer data'…", then "Successfully created…" or "Couldn't create…" — so the user knows which operation speaks. A background operation's failure lands in a persistent, actionable notification — its only channel; the fading toast is reserved for news that can afford to be missed.

## Loading

Up to a second, nothing. Past that and until ten, a spinner and what's happening: "Generating report…". Past ten, percent done and an honest estimate. The ellipsis marks in-progress.

## Forms

"Billing phone", not "Phone" under a heading — the label survives away from its page. Placeholder shows the format: "name@example.com". Helper text states the requirement ahead of the error: "Use 8+ characters with a number." Validate on leaving the field — or the moment a fixed-length field, a card number, is full; clear the error on the keystroke that fixes it, and confirm success positively.

## Tooltips

Add something, never repeat the label: "Visible only to your team." Unlabeled icon → noun phrase ("Highlighting pen"); unclear control → imperative ("Find text in this file"). Shortcut in parentheses: "Print (Ctrl+P)".

## Settings

The words live here; what to expose and which control belong to the settings doctrine. Noun-first when the setting names a thing: "Startup sound" — the toggle already says on and off. A short verb phrase when it names a behavior with no natural noun: "Run at startup". First person when it acts on the user's data or attention: "Remember my password", "Notify me when a transcript is ready". What goes is the Enable/Allow prefix, never the verb itself. The description states the consequence in one sentence: "Play a sound when Deckle starts."

## Onboarding

One concept per surface, dismissible, at the moment of relevance — the session stays the user's. Show, then get out of the way.

## Plain words

| Instead of | Say |
|---|---|
| utilize | use |
| enable | turn on |
| assist | help |
| approximately | about |
| initiate | start |
| purchase | buy |
| terminate | end |
| sufficient | enough |

## Localization

The unit is the whole sentence — "Items in the cart: {count}" — the translator reorders the placeholders, and the punctuation travels inside. A word valid as a label may be wrong as a button: one string, one grammatical role. Give the translator a context comment wherever a placeholder lives, and budget the tightest slots — buttons, tabs, headers — to double or triple.
