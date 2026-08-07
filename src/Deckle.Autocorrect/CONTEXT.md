---
name: context-deckle-autocorrect
description: "Autocorrect vocabulary — where correction may act (surfaces, enrollment), the two correction stages, protection tiers, datasets, and the learning surfaces. Read before touching correction logic, consent, or learning."
type: agent-instructions
---

# Deckle.Autocorrect — Context

Vocabulary of the system-autocorrect workstream (machine-wide bounded correction at word commit, then closed-candidate contextual correction). The Correction / Rewrite boundary that classifies what this module may do silently is system-wide language and lives in the root `CONTEXT.md`.

## Where correction may act

**Correctable surface** :
A focused text-input control where correction may act because both gates are open: its surface class is eligible and its application is enrolled. Eligibility belongs to the focused control, not the application as a whole; password controls are outside the system entirely, and non-prose controls are ineligible by default.
_Avoid_ : blocklist-only framing (exclusion classes are the safety net, enrollment is the activation gate — they are different gates).

**Enrolled app** :
An application where autocorrect is active because the user accepted enrollment. It is the user-consent gate, independent of whether the focused surface class is correctable; an app with no accepted answer stays untouched.

**Enrollment prompt** :
A passive notification asking whether to enroll an application, raised only when a correction would otherwise be possible there. It requests the consent gate; it neither grants enrollment by appearing nor applies a correction itself.
_Avoid_ : popup, dialog (it is a passive notification, not a modal).

## Taking a correction back

**Correction undo** :
The explicit act that takes a correction back through the correction inlay and records a negative learning signal. It is distinct from editing the corrected text: Backspace remains a plain Backspace and never means correction undo.
_Avoid_ : revert (the retired implicit-Backspace model), Ctrl+Z (never intercepted — it belongs to the apps).

**Correction inlay** :
The non-focusable companion surface that exposes an applied correction and its undo/redo without interrupting typing. It is both the visibility channel for silent corrections and the only surface that carries correction undo.
_Avoid_ : popup, toast, notification (it never steals focus or announces itself).

## The two stages

**Commit stage** :
The immediate correction layer that acts on the last committed word from the literal and its left context. It makes only bounded decisions and leaves ambiguity untouched; full-sentence evidence belongs to the sentence stage.
_Avoid_ : first pass (scope is the point, not order).

**Protected literal** :
A form the commit stage must leave untouched because it belongs to the primary-language lexicon, the restricted global-English layer, or the personal vocabulary. Protection is one-way: it preserves a recognized literal but does not make the protected English layer a correction target.
_Avoid_ : whitelist (protection gates correction, not observation), English lexicon as spelling authority.

**Candidate ownership** :
The provenance relationship between a candidate set and the correction policy that earned the right to propose it. Ownership keeps alternatives within the policy that justified them instead of treating every bounded form as interchangeable; the typed literal remains the explicit keep choice.
_Avoid_ : global candidate pool (candidate provenance determines correction rights, not only ranking).

**Sentence stage** :
The deferred correction layer that uses a complete sentence to arbitrate bounded alternatives the commit stage could not settle. It evaluates one whole-sentence candidate transaction and may keep the literal, select at most one supplied edit, or abstain; free regeneration belongs to Rewrite, not this stage.
_Avoid_ : reranker (one possible engine of this layer, not the layer), second pass.

**Whole-sentence candidate transaction** :
The sentence stage's indivisible decision unit: the literal sentence plus a closed set of sentences that each differ by one owned, bounded edit. The alternatives are compared together; no generated text or cascade of per-word decisions enters the transaction.
_Avoid_ : sentence rewrite (no text is generated), candidate cascade (one edit never changes what is judged next).

**Terminal-e agreement variant** :
A lexicon-backed candidate pair whose forms differ only by a final `e`, used as evidence inside a whole-sentence candidate transaction. The surface pair does not itself prove gender or earn an independent correction right.
_Avoid_ : gender rule (the surface pair does not prove grammatical category), inflection corrector (it proposes no edit on its own).

**Verified caret sentence** :
A sentence recovered from the focused surface after the observed typing stream lost continuity, accepted only when repeated reads and a local left boundary establish the same exact text. It may supply correction evidence but never observation history, authorship, or learning provenance.
_Avoid_ : observed sentence (its provenance is weaker), document context (the surrounding UIA range may cross editor and interface boundaries).

## Lexicon composition

**Domain pack** :
An activatable set of surface forms for one lexical domain and one language. It extends that language's base lexicon, so its forms become both valid literals and correction targets; it is not merely a protection list.
_Avoid_ : dictionary (overloaded — the personal dictionary is something else), protected list (a pack corrects, not only protects).

**Effective lexicon** :
A language's single runtime lexicon after its base forms, active domain packs, and word exclusions are composed. The correction engine consumes this result, never an ordered stack of dictionaries.
_Avoid_ : merged dictionaries (plural framing — the point is that there is one).

**Pack sanitization** :
A build-time filter that removes domain-pack forms whose masking cost would unacceptably weaken correction. It produces a pack already safe to compose, keeping pack conflicts out of the runtime decision path.
_Avoid_ : runtime conflict resolution (sanitization happens before a pack ships, not while correcting).

**Word exclusion** :
The user's reversible removal of one shipped form from correction's reach without disabling its domain pack or language. It subtracts from the effective lexicon and takes precedence over pack and base forms.
_Avoid_ : blocklist entry (that names the personal dictionary's suppression mechanism; exclusion targets shipped lexicon content).

## Datasets and mining

**Typing stream** :
The consented dataset that preserves the verbatim typing flow on enrolled correctable surfaces as word-shaped runs, including backward repairs. It feeds both the error corpus and the natural-language corpus while remaining distinct from raw key capture.
_Avoid_ : keylogger (it records word-shaped runs on consented surfaces, not keys), raw stream (says how it is stored, not what it is).

**Mistouch family** :
A recurrent mechanical keyboard-error class mined from the typing corpus and expressed as a deterministic generator of bounded repair candidates. It describes how input went wrong, not a spelling fault; any contextual judge may only rank its supplied candidates, never invent a repair.
_Avoid_ : typo (the broader spelling-fault class), fat-finger (informal).

**Surface profile** :
A measured portrait of how typing behaves in a correction environment, derived from observed closure and timing patterns. It is evidence used to characterize that environment, not a policy chosen by the user.
_Avoid_ : configuration (a profile records observed behavior; it does not prescribe it).

**Personal dictionary** :
The user-visible collection of learned vocabulary and correction suppressions. An adopted word becomes a protected literal; a suppression prevents one correction from recurring. Entries remain inspectable and removable, unlike the internal evidence that earned them.
_Avoid_ : learning store (the internal mechanism; this term names the visible surface).
