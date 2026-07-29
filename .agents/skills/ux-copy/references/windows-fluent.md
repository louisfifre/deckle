# Windows and Fluent

The register of a Windows app that should read like Settings or Explorer — Microsoft's first-party conventions, applied as-is.

## Voice

The register is warm and relaxed, crisp and clear, ready to lend a hand — the row it fills in the voice chart. Even when things break, the app takes responsibility.

## Casing

One rule for every string: capitalize the first word and the proper nouns, and stop there. After a slash, the capital carries over: On/Off. Windows sentence-cases; iOS and macOS title-case.

## Punctuation

| Where | Rule |
|---|---|
| Buttons, labels, checkboxes, headings, titles | end bare, without period or colon |
| Tooltips, error and dialog bodies, toasts | full sentences with periods |
| Questions | question mark, always |
| Exclamation points | celebration only |
| A label on the same line as its value | the one case for a colon |
| In-progress operations | trailing ellipsis: Installing… |
| Commas | Oxford comma |
| Spacing and dashes | one space after terminal punctuation; em dashes set tight |

## Word choice

Contractions, the natural ones — avoiding them sounds stilted; in a warning, the uncontracted "do not" carries the weight "don't" loses. Start statements with a verb. Device-agnostic verbs — select rather than click or swipe — serve every input and the screen reader too.

| Instead of | Say |
|---|---|
| error, failure | problem |
| failed to | unable to |
| illegal, invalid, bad | incorrect |
| abort, kill, terminate | stop |
| catastrophic, fatal | serious |

## Error anatomy

The heading states the problem or, better, the action — it must work with the buttons alone, because the body gets skipped. Variables — file and app names — live in the body: headings truncate. The body, when the heading needs one, adds facts and keeps the error code at the bottom for the times it is actionable. The main instruction is one present-tense sentence. Buttons answer the instruction specifically; Close covers dismissal. Object names take double quotes.

## Dialogs

Call and response: the buttons answer the heading's question with its own verbs — "Erase all data on this drive?" → Erase, Cancel. Windows orders affirmative first, Cancel last; macOS inverts — follow the host. The safest option is the default and holds focus, and the verb matches the command even when negative: confirming a Disable command says Disable. One to three words per button; four or more make it a link.

## Notifications

A toast keeps sentence case with terminal punctuation, and names its object. Onboarding tips: at most four a session — the platform's cap — each dismissible, the user always free to keep working.

## Referring to UI in text

Name the element bare — "Select Save as" — without its type word, trailing colon, or ellipsis. In-product prose describes the action rather than bolding names.

## Text accessibility

One verb per sentence wherever the sentence allows; read it aloud imagining a screen reader. Name the thing — "on the toolbar" — rather than its direction on screen. Spell out and, plus, about: symbols misread aloud. Descriptive link text, real heading levels, text that wraps on its own. Generic references go to second person or plural — singular they when grammar forces it — and disability language stays people-first.
