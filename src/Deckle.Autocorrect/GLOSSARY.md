---
name: glossary-deckle-autocorrect
description: "Autocorrect vocabulary. It covers where correction may act (surfaces, enrollment), the two correction stages, protection tiers, lexicon composition, datasets, and the learning surfaces. Read before touching correction logic, consent, or learning."
---

# Deckle.Autocorrect, Glossary

Vocabulary of the system-autocorrect workstream, which brings bounded correction to the whole machine, first when a word is committed and then by choosing among closed candidates in context. The Correction / Rewrite boundary, which decides what this module may do silently, is system-wide language and lives in the root `GLOSSARY.md`. The correction itself is defined here.

## The correction

**Correction** :
A bounded text change that repairs a span in place, drawn from a closed set of possible edits, such as restoring a diacritic, dropping a hesitation or fixing casing. It cannot add content that was not there, so it may act silently and is taken back through the correction undo.
_Avoid_ : rewrite (regenerates text, a correction only repairs), autocorrect (the module name, not the operation).

**Rewrite** :
A generative text change that regenerates a span into new text, defined in the root `GLOSSARY.md`. A correction never generates text, it only repairs what is there.

**Autocorrect** :
In ordinary use, a feature that fixes typing errors on its own. In Deckle it is the name of this module and of the product feature, while the operation they perform is a correction.

## Where correction may act

**Correctable surface** :
A focused text-input control where correction may act because both of its gates are open. Its surface class is eligible, and its application is enrolled. Eligibility belongs to the focused control, not to the application as a whole. Password controls stay outside the system entirely, and controls that do not hold prose are ineligible by default.
_Avoid_ : blocklist-only framing (exclusion classes are the safety net, enrollment is the activation gate, and they are different gates).

**Enrolled app** :
An application where autocorrect is active because the user accepted enrollment. Enrollment is the user's consent gate, independent of whether the focused surface class is correctable. An app with no accepted answer stays untouched.

**Enrollment prompt** :
A passive notification that asks whether to enroll an application, raised only when a correction would otherwise be possible there. It asks for consent. Its appearance does not enroll the app, and it applies no correction itself.
_Avoid_ : popup (it is a passive notification, not a window over the content), dialog (it is passive, not a modal).

**Blocklist-only framing** :
A framing of correction as acting everywhere except where a blocklist excludes it. In Deckle the exclusion classes are only a safety net, and enrollment is the gate that turns correction on.

**Popup** :
A small window that appears over the current content and often takes focus. Neither the enrollment prompt nor the correction inlay is a popup.

**Dialog** :
A window that asks for an answer and blocks the work until it gets one, also called a modal. The enrollment prompt is a passive notification.

## Taking a correction back

**Correction undo** :
The explicit act that takes a correction back through the correction inlay and records a negative learning signal. It differs from editing the corrected text. Backspace stays a plain Backspace and never means correction undo.
_Avoid_ : revert (the retired implicit-Backspace model), Ctrl+Z (never intercepted, it belongs to the apps).

**Correction inlay** :
The companion surface that shows an applied correction with its undo and redo, without taking focus or interrupting typing. It is how silent corrections become visible, and it is the only surface that carries correction undo.
_Avoid_ : popup (it never takes focus), toast (it never announces itself), notification (it never announces itself).

**Revert** :
In ordinary use, a return to an earlier state. Here it names the retired model in which Backspace implicitly undid a correction. The explicit act is correction undo.

**Ctrl+Z** :
The undo shortcut of the application being used. It belongs to the apps, and Deckle never intercepts it. So it never means correction undo.

**Toast** :
A short notification that slides in and leaves on its own. The correction inlay never announces itself, so it is not a toast.

**Notification** :
A message the system shows to announce something. The enrollment prompt is a passive notification, while the correction inlay never announces itself.

## The two stages

**Commit stage** :
The correction stage that acts at once on the last committed word, from the literal and its left context. It makes only bounded decisions and leaves ambiguity untouched. Evidence from the full sentence belongs to the sentence stage.
_Avoid_ : first pass (scope is the point, not order).

**Protected literal** :
A word form that the commit stage must leave untouched because it belongs to the primary-language lexicon, the restricted global-English layer or the personal vocabulary. Protection works one way. It preserves a recognized literal but does not make the protected English layer a correction target.
_Avoid_ : whitelist (protection gates correction, not observation), English lexicon as spelling authority (the English layer only protects and is never a correction target).

**Candidate ownership** :
The link of provenance between a set of candidates and the correction policy that earned the right to propose it. Ownership keeps alternatives inside the policy that justified them, rather than treating every bounded form as interchangeable. The typed literal remains the explicit choice to keep.
_Avoid_ : global candidate pool (candidate provenance determines correction rights, not only ranking).

**Sentence stage** :
The correction stage that waits for a complete sentence to settle bounded alternatives that the commit stage could not settle. It judges one whole-sentence candidate transaction and may keep the literal, select at most one supplied edit, or abstain. Free regeneration belongs to Rewrite, not to this stage.
_Avoid_ : reranker (one possible engine of this stage, not the stage), second pass (scope is the point, not order).

**Whole-sentence candidate transaction** :
The indivisible unit of decision of the sentence stage. It holds the literal sentence and a closed set of sentences that each differ from it by one owned, bounded edit. The alternatives are compared together. No generated text and no cascade of word-by-word decisions enters it.
_Avoid_ : sentence rewrite (no text is generated, see Rewrite in the root `GLOSSARY.md`), candidate cascade (one edit never changes what is judged next).

**Terminal-e agreement variant** :
A pair of candidates backed by the lexicon whose forms differ only by a final `e`, used as evidence inside a whole-sentence candidate transaction. The pair alone does not prove gender and earns no correction right of its own.
_Avoid_ : gender rule (the surface pair does not prove grammatical category), inflection corrector (it proposes no edit on its own).

**Verified caret sentence** :
A sentence read back from the focused surface after the observed typing stream lost continuity. It is accepted only when repeated reads and a local left boundary give the same exact text. It may supply evidence for correction, but never observation history, authorship or learning provenance.
_Avoid_ : observed sentence (its provenance is weaker), document context (the surrounding text may cross editor and interface boundaries).

**First pass** :
In ordinary use, the first of several passes over the same material. The commit stage is defined by its scope, the last committed word, not by coming first.

**Whitelist** :
In ordinary use, a list of allowed items. Protection decides only what correction leaves alone, not what Deckle observes.

**English lexicon as spelling authority** :
A framing of the English lexicon layer as the authority that decides how words must be spelled. That layer only protects English forms from correction and is never a correction target.

**Global candidate pool** :
One shared pool in which every candidate competes, whatever policy proposed it. In Deckle the provenance of a candidate decides its right to correct, so there is no such pool.

**Reranker** :
A model that reorders a list of candidates by score. It is one possible engine of the sentence stage, not the stage itself.

**Second pass** :
In ordinary use, a second pass over the same material. The sentence stage is defined by the scope it judges, a whole sentence, not by coming second.

**Candidate cascade** :
A chain of decisions in which each accepted edit changes what is judged next. A whole-sentence candidate transaction judges all its alternatives at once.

**Gender rule** :
A grammar rule that sets the gender agreement of a word. A terminal-e pair is only evidence and proves no grammatical category.

**Inflection corrector** :
A component that fixes word endings by itself. A terminal-e agreement variant proposes no edit on its own.

**Observed sentence** :
A sentence built from the typing stream Deckle observed as it was typed. A sentence read back from the surface has weaker provenance and is a verified caret sentence.

**Document context** :
The text around the caret in the whole document. It may cross editor and interface boundaries, so it is not a verified caret sentence.

## Lexicon composition

**Domain pack** :
An activatable set of surface forms for one lexical domain and one language. It extends the base lexicon of that language, so its forms become both valid literals and correction targets. It does more than protect.
_Avoid_ : dictionary (the word belongs to the personal dictionary), protected list (a pack corrects, not only protects).

**Effective lexicon** :
The single lexicon a language has at runtime, once its base forms, active domain packs and word exclusions are composed. Correction works on this one result, never on an ordered stack of dictionaries.
_Avoid_ : merged dictionaries (plural framing, the point is that there is one).

**Pack sanitization** :
A build-time filter that removes from a domain pack the forms whose masking cost would weaken correction too much. It yields a pack that is already safe to compose, so conflicts between packs stay out of the runtime decision.
_Avoid_ : runtime conflict resolution (sanitization happens before a pack ships, not while correcting).

**Word exclusion** :
The user's reversible removal of one shipped form from the reach of correction, without disabling its domain pack or its language. It subtracts from the effective lexicon and takes precedence over pack and base forms.
_Avoid_ : blocklist entry (it names a personal dictionary suppression, not shipped lexicon content).

**Dictionary** :
In ordinary use, a list of the words of a language. Here the word belongs to the personal dictionary, so a shipped set of domain forms is a domain pack.

**Protected list** :
A list of forms that correction must leave untouched. A domain pack also makes its forms correction targets.

**Merged dictionaries** :
Several dictionaries combined but still thought of as many. The result of composition is one lexicon, the effective lexicon.

**Runtime conflict resolution** :
A correction-time step that settles conflicts between lexicon forms. Deckle settles them before a pack ships, through pack sanitization.

**Blocklist entry** :
An entry of the personal dictionary that suppresses one correction. A word exclusion targets shipped lexicon content instead.

## Datasets and mining

**Typing stream** :
The consented dataset that keeps the verbatim typing flow on enrolled correctable surfaces, as word-shaped runs that include backward repairs. It feeds both the error corpus and the natural-language corpus, and stays distinct from raw key capture.
_Avoid_ : keylogger (it records word-shaped runs on consented surfaces, not keys), raw stream (says how it is stored, not what it is).

**Mistouch family** :
A recurring class of mechanical keyboard error, mined from the typing corpus and expressed as a deterministic generator of bounded repair candidates. It describes how input went wrong, not a spelling fault. A contextual judge may only rank the candidates it supplies and never invents a repair.
_Avoid_ : typo (the broader spelling-fault class), fat-finger (informal).

**Surface profile** :
A measured description of how typing behaves in a correction environment, drawn from observed closure and timing patterns. It is evidence that characterizes that environment, not a policy the user chooses.
_Avoid_ : configuration (a profile records observed behavior, it does not prescribe it).

**Personal dictionary** :
The collection of learned vocabulary and correction suppressions that the user can see. An adopted word becomes a protected literal, and a suppression stops one correction from coming back. Entries stay inspectable and removable, unlike the internal evidence that earned them.
_Avoid_ : learning store (the internal storage, this term names the visible surface).

**Keylogger** :
A program that records every key pressed, usually without consent. The typing stream records word-shaped runs on consented surfaces.

**Raw stream** :
In ordinary use, a stream of data kept exactly as it arrived. The name says how the data is stored, not what it is. Deckle calls this dataset the typing stream.

**Typo** :
In ordinary use, any spelling fault made while typing. It is broader than a mistouch family, which names one mechanical class of error.

**Fat-finger** :
In informal use, a keyboard error in which the finger hits the key next to the intended one. Deckle calls such a recurring class of error a mistouch family.

**Configuration** :
A set of values chosen to make a system behave a certain way. A surface profile records behavior it observed, so it is not a configuration.

**Learning store** :
The internal storage that keeps what Deckle learns. The surface the user sees is the personal dictionary.
