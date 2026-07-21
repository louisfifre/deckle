---
name: context-deckle-tests
description: "Test taxonomy — the four categories inside the automatic scope (unit, integration, observability, regression) and the two outside it (system, interactive). Read when classifying or naming a test."
type: agent-instructions
---

# Deckle tests — Context

Vocabulary of the test taxonomy. Four categories fall within the automatic test scope, runnable by an agent or by Louis via `dotnet test` without human interaction. Three categories are outside the automatic scope: they exist and are useful, but require an explicit maintainer action or an interactive workstation.

## In the automatic scope

**unit** :
Test that exercises a type or a function in isolation, without touching the file system, the network, a UI thread, or a native dependency. Natural target: pure leaf modules such as `Deckle.Composition` (ColorSpace, easing, animators), `Deckle.Chrono` (ChronoFormatter), and the pure logic of `Deckle.Core`. This is the largest and fastest layer.

**integration** :
Test that exercises a boundary with a mockable local service. The partner is simulated by a lightweight substitute controlled by the test (test HTTP server for Ollama, temporary file system for `JsonSettingsStore`, audio source simulator for the function that calls the mic). The isolation seam must be *natural* — already present in the architecture or obvious without contortion. A parasitic seam created solely for the test belongs to the "testable but unusable code" drift and is not accepted.
_Avoid_ : end-to-end, e2e (they cover different things elsewhere).

**observability** :
Test that exercises a sequence of EventSource events via an internal `TestEventListener`. Verifies that the code emits the right providers, the right event names, the right levels and keywords, and carries the expected payloads. Category native to Deckle given the weight of the EventSource pipeline (see `src/Deckle.Diagnostics/CLAUDE.md`).
_Avoid_ : log assertion, telemetry test.

**regression** :
Test added in reaction to a specific bug already fixed. Reproduces the conditions of the bug; passes because the fix holds; will fail if the fix is dropped. Its reason for being is to pin the fix in time, not to cover a nominal behavior. A regression test is typically written as a mirror of a `fix(scope): …` commit.

## Outside the automatic scope

**system** :
Test that exercises a heavy native runtime in a realistic condition — loading a 1 GB Whisper model, transcribing a reference audio file stored in the test repo, reading a Hue Entertainment payload on a real bridge. Possible to automate locally, but slow, demanding, and conditional on the availability of native artifacts and hardware. Stays in the hands of Louis or a dedicated workstation.

**interactive** :
Test that requires an interactive Windows workstation and a human or a fake human capable of presenting real conditions to the system — a real mic that picks up sound, a global hotkey that does not conflict with another app, a UIAutomation target window to validate the paste, a physical display for DXGI Output Duplication. Not automatable by an agent; Louis validates it manually via the `verify` skill in the Claude harness.

**maintenance** :
Explicit maintainer operation hosted by the test runner because it composes existing library modules and benefits from assertions — regenerate a derived lexicon, mine a collected corpus, refresh a calibrated artifact. It may read local maintainer data or write versioned/runtime artifacts, so it MUST use xUnit's `Explicit` flag and never run during an ordinary `dotnet test`. Run it deliberately from Test Explorer or with explicit tests enabled and a narrow method filter.
_Avoid_ : gesture, tool test.

## Key distinction between integration and system

The boundary between `integration` and `system` plays out on *the weight of the dependency and its substitutability*. The `Deckle.Audio.MicrophoneCapture.Probe` function that queries the audio device for its capabilities falls under `integration` if a fake audio source is substituted behind the WASAPI seam. A test that records 3 seconds of real voice in a complete loop falls under `interactive`. A test that drives Whisper on a wav stored in the test repo falls under `system`.

### Example conversation

> — Le bug d'hier sur le clipboard Win32, on le couvre comment ?
> — Test de regression. Le `OpenClipboard` retournait `false` quand un autre process tenait la session ; la fix retry trois fois ; le test simule trois échecs puis un succès et vérifie qu'on a bien copié.
> — D'accord. Et pour vérifier qu'on émet le bon `ClipboardCopied` à la fin ?
> — C'est de l'observability. Un `TestEventListener` accroché à `DeckleWhispSource`, on assert sur la séquence et sur le payload.
> — Et le micro maintenant ? Je voudrais tester qu'on ne plante pas quand il n'y en a pas.
> — Integration. On simule un device qui retourne « no input » et on vérifie le chemin d'erreur. Un test interactive prendrait un vrai micro débranché — utile mais à la main.
