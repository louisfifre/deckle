# Evidence

Why the rules are what they are. **[E]** = empirical (testing, eyetracking, controlled study) · **[S]** = standard/normative · **[C]** = expert convention. Convention is not worthless — but when a convention and a finding conflict, the finding wins.

## Reading behavior

- **[E]** People scan, they don't read: ~79% scan, ~16% read word-by-word; F-pattern concentrates fixation on first lines and first words. Hence front-loading. — NN/g eyetracking, 20+ years replicated. https://www.nngroup.com/articles/f-shaped-pattern-reading-web-content/
- **[E]** Concise + scannable + objective writing measured **+124% usability** vs promotional "marketese" (concise alone +58%). — NN/g. https://www.nngroup.com/articles/concise-scannable-and-objective-how-to-write-for-the-web/
- **[E]** Sentence length drives comprehension: >90% at ~14-word average, collapsing under 10% at 43 words. GOV.UK's hard limit is 25. — Wylie, via GOV.UK. https://insidegovuk.blog.gov.uk/2014/08/04/sentence-length-why-25-words-is-our-limit/
- **[E]** Structure beats surface: center-embedded clauses (qualifiers nested mid-sentence) are the dominant driver of poor comprehension and recall — for experts too; even lawyers prefer plain English. Linearize, don't nest. — MIT (Gibson/Martinez/Mollica). https://news.mit.edu/2023/new-study-lawyers-legalese-0529
- **[E]** Plain language helps *all* literacy levels, experts included; 80–97% preference for plain variants in tested substitutions. — GOV.UK, plainlanguage.gov compilations. https://www.plainlanguage.gov/guidelines/words/use-simple-words-phrases/
- **[C→caveat]** Readability formulas (Flesch etc.) measure proxies (word/sentence length), were never validated on functional text, and correlate weakly with task performance. Use a grade score as a smoke alarm, never as a target. — Redish. https://redish.net/wp-content/uploads/Redish_on_Readability_Formulas.pdf

## Tone

- **[E]** Four tone dimensions (funny↔serious, formal↔casual, respectful↔irreverent, enthusiastic↔matter-of-fact); tone measurably shifts brand perception. — NN/g, n=50, p<0.05. https://www.nngroup.com/articles/tone-of-voice-dimensions/
- **[E]** Trustworthiness explains **52%** of desirability variance; friendliness ~8%. Casual/conversational/moderately-enthusiastic profiles score best; playfulness can undermine trust in serious domains. — NN/g, n=100. https://www.nngroup.com/articles/tone-voice-users/

## Errors and apology

- **[E]** Unclear errors push users to abandon the task; a stated recovery path sustains persistence. — NN/g testing. https://www.nngroup.com/articles/error-message-guidelines/
- **[E]** Users want acknowledgement + apology + explanation + suggested fix in failures. — Fraunhofer preference study. Explanatory apologies beat rote and empathic ones (p<0.05); over-apology cheapens. — 2025 apology study. https://arxiv.org/html/2507.02745v1
- **[S]** Microsoft rations "please"/"sorry" and bans blame words with exact replacements; one message per detectable cause; prevention first. — Win32 error guidelines. https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error
- **[S]** WCAG 3.3.1 (A): auto-detected input errors must be identified **in text**. WCAG 3.3.3 (AA): suggest the correction when known. One well-written message satisfies both. https://www.w3.org/WAI/WCAG21/Understanding/error-identification.html
- **[S]** ISO 9241-110 use-error robustness = avoidance → tolerance → recovery, in that order. The message is the last resort, not the plan.

## Forms

- **[E]** Placeholder-as-label failed in **every** Baymard usability test and carries seven documented harms in NN/g's testing (vanishing context, unverifiable input, broken error recovery, skipped fields…). Persistent label above the field. https://www.nngroup.com/articles/form-design-placeholders/ · https://baymard.com/blog/mobile-forms-avoid-inline-labels
- **[E]** Validation timing: on blur (or at full fixed length); premature validation actively hostile; clear the error on the fixing keystroke; give positive confirmation. — Baymard. https://baymard.com/blog/inline-form-validation

## Interruption and feedback

- **[E]** Interrupted work takes on average **23 min 15 s** to resume; interruptions raise stress. Every modal and notification spends from this budget. — Mark et al., CHI 2008. https://ics.uci.edu/~gmark/chi08-mark.pdf
- **[E]** Response-time thresholds: 0.1 s feels instant; 1 s keeps flow; 10 s is the attention limit — spinner for 2–10 s, percent-done beyond. — Nielsen, classic HCI. https://www.nngroup.com/articles/response-times-3-important-limits/
- **[E]** Toast errors go unseen (documented 5-minute wait on a faded toast). Modals for must-resolve only. https://www.nngroup.com/articles/indicators-validations-notifications/

## Buttons and dialogs

- **[E]** Vague CTAs ("Get started") stall users in testing; specific labels don't. 4 Ss for links are eyetracking-derived. https://www.nngroup.com/articles/get-started/ · https://www.nngroup.com/articles/better-link-labels/
- **[C]** Verb labels over OK/Yes/No: users act on the button alone — a rule stable since NeXTSTEP (1993). Overused confirmations breed automatic dismissal. https://www.nngroup.com/articles/confirmation-dialog/
- **[C]** Button *order* is platform convention (Windows affirmative-first, macOS affirmative-last); following the host beats optimizing. https://www.nngroup.com/articles/ok-cancel-or-cancel-ok/

## Localization

- **[E]** Text expansion is inverse to length: strings ≤10 chars grow +200–300% in translation; buttons/tabs/headers blow up first. — IBM via W3C. https://www.w3.org/International/articles/article-text-size
- **[S]** Concatenation breaks on word order, plurals (Russian has 3 forms), gender agreement, declension, punctuation; author whole sentences with reorderable placeholders and real plural categories. — Microsoft Globalization. https://learn.microsoft.com/en-us/globalization/internationalization/concatenation

## Accessibility & cognition

- **[S]** WCAG 3.1.5 (AAA): lower-secondary reading level (~8th–9th grade) or provide a simpler version.
- **[S]** W3C COGA: ~1,500-most-common-words vocabulary, literal language (no idioms/metaphors/sarcasm), one point per sentence, unambiguous dates ("March 4, 2019"), one instruction per line. https://www.w3.org/TR/coga-usable/design_guide.html

## Known divergences

Held positions where trustworthy sources disagree — pick per context, don't pretend consensus:

- **Negative contractions**: GOV.UK writes "cannot" (misread risk when scanning); Material/Polaris/Atlassian write "can't", with "do not" reserved for emphasis in warnings. Default: contractions, but expand for high-stakes warnings.
- **Apology**: Polaris bans "sorry" outright; research finds explanatory apologies helpful for genuine faults. Resolution in voice-and-tone.md: rare, at-fault-only, always explanatory.
- **Casing**: sentence case is the modern majority and the Windows norm; Apple leaves the choice per app (and title-cases on iOS/macOS). Resolve to platform.
- **Word-count "comprehension percentages"** repeated in industry checklists (≤8 words → ~100%…) are readability lore, not cited studies — the GOV.UK/Wylie sentence-length data is the citable anchor.
