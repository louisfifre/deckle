---
name: context-deckle-autocorrect
description: "Autocorrect vocabulary — where correction may act (surfaces, enrollment), the two correction stages, protection tiers, datasets, and the learning surfaces. Read before touching correction logic, consent, or learning."
type: agent-instructions
---

# Deckle.Autocorrect — Context

Vocabulary of the system-autocorrect workstream (machine-wide diacritics restoration first, conservative typo correction second). The Correction / Rewrite boundary that classifies what this module may do silently is system-wide language and lives in the root `CONTEXT.md`.

## Where correction may act

**Correctable surface** :
A text-input context where the system autocorrect is allowed to act. Two gates, both required. First, the *surface class* must be correctable: password fields are outside the system entirely — never corrected, keystrokes never observed or buffered (hard rule, not a setting) — and non-prose contexts (terminals, code editors, full-screen games) are excluded by default. Classes are judged per surface (the focused control), not per application: an embedded terminal inside an otherwise prose app is still excluded, to the extent the surface can be identified. The exact class inventory follows the background research. Second, the *application* must be enrolled.
_Avoid_ : blocklist-only framing (exclusion classes are the safety net, enrollment is the activation gate — they are different gates).

**Enrolled app** :
An application where autocorrect is active because the user accepted the enrollment prompt there. Enrollment is asked once per app and remembered; an app never asked, or never answered, stays non-enrolled and untouched.

**Enrollment prompt** :
The notification raised the first time the system *could* correct something in a non-enrolled app — never on mere app launch, so apps where no prose is ever typed never see it. It does not block, steal focus, or rewrite anything; corrections stay withheld until the user opts in, and ignoring the prompt is a valid answer. Candidate refinement, noted not committed: applying the withheld corrections retroactively when consent arrives late.
_Avoid_ : popup, dialog (it is a passive notification, not a modal).

## Taking a correction back

**Correction undo** :
The explicit act that takes a correction back, carried by the correction inlay — never by the keyboard. Backspace is always a plain Backspace: backing into a corrected word means editing it, not disputing the correction. Replaces the retired implicit-Backspace revert, whose misfires (a deleted comma read as an undo) broke the trust the corrector exists to earn. An undo is also the negative learning signal — it writes the suppression that keeps a correction from coming back on its own; the exact learning semantics are open.
_Avoid_ : revert (the retired implicit-Backspace model), Ctrl+Z (never intercepted — it belongs to the apps).

**Correction inlay** :
The small non-focusable surface that sits above the active text field and reveals on pointer proximity (the Hue-window reveal pattern), carrying the last applied correction and its undo/redo. The only place a correction is taken back, and the only visibility corrections get — the typing flow itself stays free of visual effects by decision. Contents and depth (one correction or a short history) are open.
_Avoid_ : popup, toast, notification (it never steals focus or announces itself).

## The two stages

**Commit stage** :
The instantaneous correction layer, acting at word commit with left context only — conservative, bounded, imperceptible. What it cannot decide it leaves alone, and it never touches anything behind the last committed word. A word the user has reopened and retyped is exempt from this stage for that occurrence — the deliberate keystroke asserts intent; only the sentence stage, deciding from full context, may still revise it.
_Avoid_ : first pass (scope is the point, not order).

**Protected literal** :
A form the commit stage must never touch because it is valid in a recognized lexicon. Three tiers protect, one architecture: the *primary language* (swappable by design; French today, the large inflected lexicon), the permanent *global-English layer* (the same whatever the primary language), and the *personal vocabulary*. The English layer is deliberately *restricted* — a fixed seed of technical globish and brand names, plus what the user's own usage adopts (dictation transcriptions are a prime source) — never a full English dictionary, which would shield too many mangled French words. Protection is one-way: a valid English form is never corrected, but nothing is corrected *toward* English and English spelling is not repaired. Whether an English-shaped token was in fact a mangled French word is the sentence stage's call, made from the whole sentence.
_Avoid_ : whitelist (protection gates correction, not observation), English lexicon as spelling authority.

**Sentence stage** :
The deferred correction layer that runs once at sentence close (the terminal punctuation commits) and re-reads the whole sentence, revising committed words inside that sentence only. Owns the decisions only context can make: code-switching, ambiguous pairs, escalation of the hardest faults — and its candidate set always includes the form the user actually typed, so it can silently take back a commit-stage correction. The sentence becomes final the moment the verdict is rendered; a verdict arriving after any keystroke since the close is dropped and counted, never woven into live typing, and an abandoned sentence (field change, Enter, long silence) dies unrevised. Its revisions surface in the correction inlay like any correction.
_Avoid_ : reranker (one possible engine of this layer, not the layer), second pass.

## Datasets and mining

**Typing stream** :
The everything-capture dataset: the verbatim flow of what is typed on enrolled correctable surfaces, recorded as runs — a run accumulates while typing flows forward and closes the moment a backward repair begins, the next run resuming from the repair point. Reading the runs in order restores everything: faulty forms as they stood on screen, what was erased, what was retyped, and clean sentences whole. Serves two corpora at once — the error corpus and the natural-language corpus (the user's own way of writing). Same consent envelope and JSONL family as the other autocorrect datasets, one `kind` among them; password surfaces stay outside the system entirely, as everywhere.
_Avoid_ : keylogger (it records word-shaped runs on consented surfaces, not keys), raw stream (says how it is stored, not what it is).

**Mistouch family** :
A recurrent mechanical keyboard-error class mined from the typed-sentence corpus — a wrong key hit near the intended one (`;` for the apostrophe → `qu;il`), a dropped space after a comma — as opposed to a spelling fault. A family is discovered offline by mining, then expressed as a deterministic detector-generator that proposes bounded repair candidates. Routing follows ambiguity: a family with a single possible reading repairs instantly at the commit stage; a family with several readings generates candidates for the sentence stage, where the judge decides. Commit-stage eligibility takes three cumulative conditions — the trigger is a non-word impossible in every lexicon tier, the repair is unique, and left context suffices. The generative model never proposes repairs itself — it only scores mined, bounded candidates.
Families follow the personal dictionary's adoption discipline: a family activates on its own past an evidence threshold calibrated on the corpus, stays inspectable and removable, and an undo through the correction inlay writes the explicit suppression that keeps it from coming back. One exception, one-time: the very first mined batch is reviewed by the maintainer before the door turns automatic for good.
_Avoid_ : typo (the broader spelling-fault class), fat-finger (informal).

**Surface profile** :
The per-application portrait of how typing behaves there, computed from the corpus closure and timing statistics: how sentences end (sentence boundary, Enter, interruption), at what rhythm, with what pauses. Names where the sentence stage arrives too late (Enter-heavy surfaces) and calibrates the pause pass. A measured artifact, never a user-exposed setting.
_Avoid_ : app profile (a surface is the focused control's context, not the whole app), configuration (nothing here is set by hand).

**Pause pass** :
An anticipated sentence-stage pass triggered mid-sentence by a typing pause, on surfaces whose profile says Enter usually closes — so corrections land before the Enter that sends the text. The pause threshold is calibrated per surface from the user's own timing statistics. The true-closure pass still reviews the whole sentence afterwards; since the typed original always stays among the candidates, a premature verdict can be silently taken back — unless Enter left first, a residue to measure.
_Avoid_ : partial close (the sentence stays open), timeout (it is an opportunity, not a deadline).

**Personal dictionary** :
The user-visible surface of everything autocorrect has learned — adopted words and suppressed corrections. Adoption is earned, never granted on sight: recurrence across distinct days plus a cleanliness gate (typed verbatim by the user, never reopened-and-retyped, surface-clean). An entry carries one of three categories, which exist only because they change protection: anglicism (case-insensitive), proper noun (case-sensitive — the capital is part of what is protected), other. Inspectable and editable by principle: a consultable list, per-word removal, full purge. Suppression is an explicit entry (a blocklist), never the mere erasure of a counter — a removed word must not come back on its own. Candidate bridge to the ASR personal lexicon (shared learning, not yet committed).
_Avoid_ : learning store (the internal mechanism; this term names the visible surface).
