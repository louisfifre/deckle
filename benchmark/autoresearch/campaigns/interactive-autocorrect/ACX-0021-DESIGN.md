---
name: acx-0021-delayed-range-owned-correction
description: "Frozen design and safety matrix for delayed correction of a closed sentence while typing continues."
type: benchmark-plan
module: benchmark/autoresearch/campaigns/interactive-autocorrect
---

# ACX-0021 — delayed range-owned closed-sentence correction

## Product question

Can Deckle correct a submitted positional edit in completed sentence A after the
user has started sentence B, without replaying deletion or replacement keys at
the live caret?

The experiment is deterministic and model-free. It studies application
ownership only. Production autocorrect is neither referenced by the first
Playground capability nor changed by any phase.

## Architecture boundary

The semantic transaction remains one exact UTF-16 literal and one supplied
positional edit. The decider owns no write authority. A fresh application lease
must authorize the exact target, range, document prefix, selection, composition,
and surface capability immediately before a range-owned write.

Current-caret `SendInput`, `Backspace`, `Delete`, paste, and selection-replacement
bursts are ineligible. A successful API return is not success: the exact document
postcondition and semantic caret/selection position must both be observed.

## Frozen experiment phases

ACX-0021 stays open until all three phases are attempted and recorded as valid,
invalid, or genuinely blocked by their own preregistered gates. A valid phase A
cannot select an integration or silently discharge phases B and C.

## Phased surface matrix

| Phase | Surface/path | Range ownership | First purpose | Admissible claim if valid |
|---|---|---|---|---|
| 1 | In-process WinUI `RichEditBox` through `RichEditTextDocument.GetRange` and `ITextRange.SetText` | Direct TOM range; no selection mutation requested | Establish the interaction and the lease/postcondition protocol in the Playground | Capability and measured document-postcondition timing for this one controlled WinUI surface only |
| 2 | Isolated external classic Edit and versioned RichEdit controls | `EM_SETSEL` plus `EM_REPLACESEL` changes the active selection; TOM availability across a process boundary is unestablished | Permanently destructive negative control, separately stratified by Edit/RichEdit version and Unicode mode; external TOM discovery is a distinct capability stratum | Race/corruption characterization only; this selection path can never answer the product question or become a production candidate |
| 3 | Minimal TSF text service and `ITfContext.RequestEditSession` plus `ITfRange.SetText` | Context-owner write lock and tracked TSF range | Test the high-trust external path against the same matrix | Only surfaces actually reached by the prototype, with native deployment and IME costs explicit |

UI Automation `TextPatternRange` is read-only and invalidates when the container
changes. `ValuePattern.SetValue` replaces the complete value. Neither is an
eligible range-write path.

Passing phase 1 does not select a machine-wide production integration. Failing a
phase is scoped to the exact surface/path tested and does not refute the other
phases.

## Phase-1 application lease

The Playground uses one owned `RichEditBox` and one synthetic positional edit.
The initial fixture is `Il est la.` with the exact equal-length UTF-16 edit
at absolute start 7, length 2, replacement `là`; equal length isolates caret
ownership before length-changing cases
exercise semantic selection shifts.

At arm time the lab captures, without persistence:

- target instance generation;
- exact body text excluding TOM's undeletable final paragraph mark;
- an independent inward-gravity sentence `ITextRange` as diagnostic evidence of
  TOM tracking behavior, never as write authority;
- edit start, UTF-16 length, literal, and replacement;
- exact selection endpoints, selection options, and active focus;
- composition-neutral and writable state;
- monotonic arm timestamp and configured delay.

The synthetic fixture places its degenerate terminal selection with
`SelectionOptions.AtEndOfLine` before clearing native history. TOM assigns two
visual affinities to an ambiguous line-boundary position; explicitly selecting
the end-of-line affinity makes the programmatic fixture match a user caret at
the visible end of the preceding line. Apply, Undo, and Redo still require the
complete option set to round-trip exactly. The affinity bit is not masked or
ignored by the postcondition.

The lease is a monotonic state machine:

- `armed_safe`: only an exact append-at-end transition is admitted;
- `poisoned`: any focus loss, window deactivation, composition start or unknown
  composition state, non-degenerate or non-terminal selection, target-prefix
  mutation, non-append transition, control regeneration, or unsupported TOM
  mapping permanently refuses this attempt even if the user later restores the
  same visible state;
- `cancelled`: reset, re-arm, navigation, unload, disposal, or a newer generation
  permanently supersedes it;
- `releasing`: one synchronous UI-thread precheck/write/postcheck/undo-verification
  section; its own document and selection events cannot count as user changes;
- `completed`: terminal applied, abstained, cancelled, or integrity-failure state.

Terminal lifecycle cancellation has precedence over every poison transition:
reset, re-arm, navigation, unload, disposal, or a newer generation first records
`cancelled`. Cleanup may then set focus/composition internals to unknown, but it
cannot replace, precede, or reclassify that terminal outcome.

Every relevant editor text, selection, focus, window-activation, lifecycle, and
composition transition updates the state while armed. Re-arming is the only way
to recover from poison or cancellation. A final-state equality check can never
unpoison an attempt.

### Frozen focus, activation, writable, and composition authorities

| Authority | Initialization and synchronous gate | Monotonic transition rule |
|---|---|---|
| Editor focus | Arm and release both require `FocusManager.GetFocusedElement(editor.XamlRoot)` to be the exact editor instance and `editor.FocusState != Unfocused` | `LostFocus` poisons immediately. `GotFocus` can establish eligibility only before a new arm; it never repairs an existing lease. |
| Playground activation | `PlaygroundWindow.Activated` owns an activation generation and active/deactivated state exposed through typed `PlaygroundShell` callbacks. Arm and release require active state and the exact captured generation. Initial state is unknown until the shell publishes it. | Every activation event after arm changes the generation and poisons, including deactivation followed by reactivation. A final active state cannot repair it. |
| Writable state | Arm and release require `RichEditBox.IsReadOnly == false`; the adapter registers a callback on `IsReadOnlyProperty`. | Any writable/read-only property transition after arm poisons, including read-only followed by writable. |
| IME composition | Page construction, load, focus loss, and activation loss set `unknown`. Unload first performs terminal lifecycle cancellation, then sets the internal state to `unknown` without reclassification. A focus gain before arming establishes `known_neutral`; `TextCompositionStarted` establishes `active`; `TextCompositionEnded` returns `known_neutral` only from `active`. Arm and release require `known_neutral`. | `Started` poisons an armed lease. `Changed` outside `active`, `Ended` outside `active`, missed/out-of-order events, focus loss, or activation loss set `unknown` and poison. Composition events are never suppressed during programmatic TOM work; one during `releasing` is an integrity failure. |

Only the correction/Undo/Redo operation's own text and selection notifications
are suppressed from armed user-transition accounting while state is `releasing`.
Lifecycle, activation, writable, focus, and composition notifications are never
suppressed.

At release, all of these gates must pass in the same UI-thread callback:

1. the page, target instance, and arm generation are still current;
2. the lease has remained `armed_safe`; the Playground window is still active;
   the editor is the XAML focused element, writable, and in a known-neutral IME
   state;
3. the selection is one degenerate caret at the end of the current body;
4. the complete armed body is an exact prefix of the current body, so only
   forward append after sentence A is admitted;
5. the retained diagnostic sentence range still equals the frozen sentence;
6. the frozen submitted .NET UTF-16 coordinates still map bidirectionally to TOM,
   and a fresh token range created from those verified coordinates exactly equals
   the submitted literal;
7. edit boundaries are members of the frozen `StringInfo` text-element boundary
   set, exercised with surrogate pairs, combining sequences, variation selectors,
   ZWJ emoji, and regional-indicator flags;
8. replacement through that fresh non-selection TOM range is enclosed in one
   undo group, with `EndUndoGroup` guaranteed by `finally`;
9. the complete body immediately equals the pre-write body with exactly the
   submitted range replaced;
10. the caret/selection immediately equals its expected semantic position after
   the edit delta, with unchanged selection options and focus.

Phase A then performs an immediate controlled undo/redo round trip before leaving
the synchronous release section: Undo must restore the exact pre-write body and
its expected selection state without changing sentence-B text or selection; Redo must
restore the exact declared edit and final selection state. The post-redo document
is the delivered result. Any undo, redo, body, focus, or selection mismatch is an
integrity failure. This proves only the controlled RichEditBox history observed
in phase A. It does not prove that the wider undo stack was preserved, ordered,
or partitioned correctly; general application undo-history safety remains
unclaimed.

Any failed precondition abstains before writing. Any failed postcondition is an
integrity failure. Phase A performs no automatic rollback after a failed
postcondition: a second blind write could compound the damage. Physical input cannot
be processed inside the synchronous UI-thread precheck/write/postcheck section;
queued input after that section is outside the measured atomic interval.

### TOM-to-.NET mapping contract

The adapter freezes `TextGetOptions.None` and `TextSetOptions.None`. It reads the
TOM story bounds, obtains the undeletable final paragraph range from those bounds,
and treats it as a separate adapter artifact; it never trims an assumed final
`.NET char`. The candidate body range must round-trip exactly and its TOM length
must equal the submitted .NET string's UTF-16 length. Prefix ranges at candidate
start and end must round-trip to strings whose `.Length` equals the corresponding
submitted index. CR, CRLF, LF, U+2028, U+2029, supplementary scalars, and final-EOP
boundaries form mandatory calibration strata. Any normalization or length
disagreement is `unsupported_mapping` and abstains before write.

TOM documents character positions as volatile after preceding edits. The frozen
submitted coordinates remain authoritative only because the monotonic lease
admits append strictly after the complete armed body. The tracked sentence range
is compared as diagnostic evidence; it can never relocate or authorize the write.

## Distinct outcomes

- `applied`: the one submitted edit and every postcondition are observed;
- `abstained`: no write was attempted because one named lease gate failed;
- `integrity_failure`: a write was attempted but the observed text or
  caret/selection/undo postcondition differed;
- `cancelled`: a newer arm, reset, navigation, unload, or disposal superseded the
  pending attempt.

KEEP is not produced by this application-only experiment. It remains a semantic
decision upstream and is not collapsed into abstention here.

## Frozen phase-1 matrix

| Scenario | Expected outcome |
|---|---|
| Sentence B appended while the delay runs | Apply exactly the saved edit; appended text and live caret remain semantically unchanged |
| No continuation typing | Apply exactly the saved edit |
| Focus leaves the editor | Abstain before write |
| Target reset, unloaded, or re-armed | Cancel before write |
| Selection becomes non-degenerate | Abstain before write |
| Caret moves away from the current body end | Abstain before write |
| Any armed-prefix or target-range unit changes | Abstain before write |
| Text is inserted, removed, or replaced anywhere except append-at-end | Abstain before write |
| Editor becomes read-only | Abstain before write |
| IME composition is active or composition state is uncertain | Abstain before write |
| Any disallowed transition occurs and visible state later returns to its armed value | Abstain because poison is permanent |
| Emoji, combining, variation-selector, ZWJ, or flag sequences occur before or after the edit | Preserve them exactly; edit only at `StringInfo` boundaries |
| CR, CRLF, LF, U+2028, U+2029, or final-EOP mapping is not exact | Abstain as unsupported mapping |
| Replacement length changes in deterministic protocol tests | Shift the semantic caret by the exact UTF-16 delta |
| TOM text postcondition differs | Integrity failure; never count as applied |
| TOM selection/caret/focus/options postcondition differs | Integrity failure; never count as applied |
| Undo changes sentence-B text/selection or Redo fails to reconstruct the edit | Integrity failure; never count as applied |

Formatting preservation, protected/password surfaces, target destruction and
recycling, UIPI, external application focus races, cross-process timing, and TSF
composition ownership remain required in phases B and C. Plain-text phase A
cannot detect formatting damage and makes no formatting claim. Unit tests may
prove protocol arithmetic but cannot prove WinUI/TOM runtime behavior.

Phase A demonstrates only serialized UI-thread atomicity. It does not prove
uninterrupted physical typing, queued-input ordering, or a visible-frame endpoint.
Its one Undo/Redo round trip does not prove preservation or ordering of the wider
undo history.

## Measurements retained by the Playground

The page keeps an in-memory, text-free record per attempt:

- monotonically increasing attempt index;
- configured delay, actual release delay, and overshoot;
- precheck-to-postcondition duration;
- appended UTF-16 unit count and text-change event count while armed;
- selection endpoints before and after, expressed as lengths only;
- edit UTF-16 length delta;
- outcome and one closed-vocabulary reason;
- exact-text, exact-selection, focus, writable, composition, and target-generation
  gate booleans.

No typed text, literal hash, application text, or GPU claim is emitted or
persisted. Phase A is synthetic-only. Exact timings, lengths, and event cadence
remain potentially identifying, so a privacy review and coarsened content-free
export are mandatory before Louis's physical measurements count as retained raw
evidence. The programmatic correction, undo, and redo events are marked internal
and excluded from the armed user-change counters.

## Validation before phase-1 preregistration

- behavior tests cover every frozen protocol row, including surrogate pairs,
  combining sequences, variation selectors, ZWJ sequences, flags, append-only
  continuation, poison-then-restore, prefix/range drift, selection,
  focus, composition, read-only state, generation cancellation, length-changing
  caret shifts, and postcondition mismatch;
- the adapter explicitly reconciles TOM's final paragraph mark and verifies
  the fresh write range against frozen coordinates rather than trusting a tracked
  range or delayed integer offsets without mapping proof;
- focused Playground test project passes;
- full `Deckle.Autocorrect.Tests` passes unchanged;
- global `Deckle.Tests.sln` Debug x64 build has zero warnings and errors;
- independent static audit verifies no `SendInput`, current-selection replacement,
  production autocorrect reference, text persistence, or claim-boundary leak;
- tracked implementation is committed and the worktree is clean before the
  phase-1 execution plan is hashed and appended to the campaign ledger.

## Claim boundary

Phase 1 can establish only deterministic protocol behavior and observed TOM
document/caret postconditions inside Deckle's owned WinUI `RichEditBox`. It cannot
establish external-control compatibility, TSF feasibility, production safety,
field quality, applied-correction precision, physical-switch-to-frame latency,
UIA safety, cross-process atomicity, IME safety beyond explicit abstention, or
general undo/formatting preservation. No inference or GPU work occurs.

## Phase-C TSF privacy and deployment boundary

Before any TSF implementation, ACX-0021C must separately preregister:

- isolated per-test registration and guaranteed unregistration of the native
  in-process COM text service, including cleanup after activation failure;
- exact bitness, packaging, signing, language-profile/category, Store, and test-
  machine assumptions;
- activation limited to the named isolated test host first, with no production or
  daily-app activation;
- password, protected, read-only, unknown-sensitivity, and active-composition
  vetoes before buffering or writing;
- zero text logging and no persisted range content;
- asynchronous write-session handling, lock conflicts, disconnected contexts,
  composition rejection, range-not-covered, teardown, and target destruction;
- an explicit security review before widening activation beyond the isolated host.

The TSF service's access to in-process text is a new privacy/security authority,
not an implementation detail inherited from phase A.
