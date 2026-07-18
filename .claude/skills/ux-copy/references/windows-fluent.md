# Windows / Fluent mechanics

Microsoft's first-party conventions — the target register for a Windows app that should read like Settings or Explorer. Sources: Microsoft Writing Style Guide, Windows apps writing style, Fluent 2 content design, Windows Admin Center UI text guide, Win32 error guidelines (URLs in evidence.md).

## Voice

Warm and relaxed · crisp and clear · ready to lend a hand. "Bigger ideas, fewer words." Write for scanning first, reading second. Even when things break, the app takes responsibility — never the user.

## Casing

- Sentence case for **every** string: buttons, checkboxes, labels, titles, headings, menus. Capitalize only the first word and proper nouns.
- Never Title Case, never ALL CAPS for emphasis, never all-lowercase as styling. "On/Off" — capitalize after a slash if the word before is capitalized.
- Fluent 2 platform note: Windows/Android/Web = sentence case; iOS/macOS = title case.

## Punctuation

| Where | Rule |
|---|---|
| Buttons, labels, checkboxes, headings, titles | No terminal period, no trailing colon |
| Tooltips, error bodies, dialog bodies, toasts | Full sentences with periods |
| Questions (dialog headings, help links) | Question mark, always |
| Exclamation points | Avoid; celebration only |
| Labels on the same line as their value | The one case for a colon |
| In-progress operations | Trailing ellipsis ("Installing…") |
| Commas | Oxford comma always; one space after terminal punctuation; no spaces around em dashes |

## Word choice

- Contractions: it's, you'll, we're — avoiding them sounds stilted. Don't invent contractions to save space.
- Start statements with a verb; cut "you can", "there is/are/were".
- Win32 replacement table: error/failure → problem · failed to → unable to · illegal/invalid/bad → incorrect · abort/kill/terminate → stop · catastrophic/fatal → serious.
- Device-agnostic verbs: "select", not "click"/"swipe" (also serves screen readers).

## Error anatomy

- **Heading**: brief, states the problem or (better) the action; relates to the button so it works even if the body is skipped; never generic ("Something went wrong"); **no variables in headings** — file/app names go in the body (headings truncate).
- **Body**: skip it if the heading suffices; never restate the heading; facts first. `Error code: ####` at the bottom, only when actionable.
- **Main instruction**: one sentence, present tense, sentence case, no final period unless a question. Templates: `[subject] can't [action]` · `[subject] can't [action] because [reason]` · `"[object]" is currently unavailable` · `You don't have permission to access "[object]"`.
- **Buttons**: a specific response to the instruction; otherwise "Close" — never "OK"/"Done" for dismissal. Object names in double quotes.
- Target register: "We couldn't upload the picture. If this happens again, try restarting the app. But don't worry — your picture will be waiting when you come back."

## Dialogs

- Call-and-response: buttons answer the heading's question, verbatim verbs ("Erase all data on this drive?" → [Erase] [Cancel]).
- Windows button order: affirmative first, Cancel last (macOS inverts — follow the host platform). Leftmost = encouraged action, rightmost = conservative.
- Default focus on the safest, least destructive option; button verb matches the command even when negative (confirming a Disable command → "Disable").
- 1–3 words per button; four or more → make it a link.

## Notifications

- Toast: sentence case, terminal punctuation, includes the object variable.
- Progression: "Creating the volume 'Customer data'…" → "Successfully created the volume 'Customer data'." / "Couldn't create the volume 'Customer data'."
- Onboarding pop-ups/tips: ≤ 4 per session, always dismissible, never blocking.

## Referring to UI in text

In docs: **bold** the element name, sentence case, drop the trailing colon/ellipsis and the element-type word ("Select **Save as**", not "Click the **Save as…** button"). Keyboard shortcuts bold with no spaces: **Ctrl+Alt+Del**. In-product prose avoids bold — describe the action instead.

## Accessibility of text

- One verb per sentence where possible; read it aloud imagining a screen reader.
- No directional-only cues ("the panel on the left") — name the thing ("on the toolbar").
- Spell out "and", "plus", "about" — screen readers misread symbols.
- Descriptive link text, never "Click here"; real heading levels, never bold-as-heading; no forced line breaks.
- Gender-neutral generic references (rewrite to second person or plural; singular "they" if unavoidable); people-first disability language; no directional, militaristic, or ableist idioms.
