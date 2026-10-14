---
name: glossary-deckle-tests
description: "Test taxonomy. It names the four categories inside the automatic scope (unit, integration, observability, regression) and the three outside it (system, interactive, maintenance). Read when classifying or naming a test."
---

# Deckle tests, Glossary

Vocabulary of the test taxonomy. Four categories fall within the automatic test scope, which `dotnet test` runs without human interaction. Three categories fall outside it. They exist and are useful, but they need an explicit maintainer action or an interactive workstation.

## In the automatic scope

**unit** :
A test that exercises a type or a function in isolation, without touching the file system, the network, a UI thread or a native dependency. It is the largest and fastest layer.

**integration** :
A test that exercises a boundary with a local service that the test can stand in for. A lightweight substitute controlled by the test plays the partner, such as a test HTTP server or a temporary file system. The seam that isolates it must be *natural*, already present in the architecture or obvious without contortion. A seam created only for the test does not qualify.
_Avoid_ : end-to-end, e2e (they cover different things elsewhere).

**observability** :
A test that exercises a sequence of EventSource events through an internal `TestEventListener`. It checks that the code emits the right providers, event names, levels, keywords and payloads. The category is native to Deckle because of the weight of its EventSource pipeline.
_Avoid_ : log assertion, telemetry test.

**regression** :
A test added after a specific bug was fixed. It reproduces the conditions of the bug, passes because the fix holds, and fails if the fix is dropped. It exists to pin the fix in time, not to cover a nominal behavior.

**End-to-end** (also e2e) :
In ordinary use, a test that drives the whole product from user input to final output. The word means different things from one project to another, so Deckle does not use it as a category.

**Log assertion** :
A check on the text a program writes to its logs. A test that checks emitted EventSource events is an observability test.

**Telemetry test** :
In ordinary use, a test of collected usage data. A test that checks emitted EventSource events is an observability test.

## Outside the automatic scope

**system** :
A test that exercises a heavy native runtime in realistic conditions, such as loading a 1 GB Whisper model, transcribing a reference audio file stored in the test repo, or reading a Hue Entertainment payload on a real bridge. It can be automated locally, but it is slow, demanding, and depends on native artifacts and hardware being available. It stays in the hands of Louis or a dedicated workstation.

**interactive** :
A test that needs an interactive Windows workstation and a human, or a stand-in for one, able to present real conditions to the system, such as a real microphone that picks up sound, a global hotkey that must not conflict with another app, a UIAutomation target window to validate the paste, or a physical display for DXGI Output Duplication.

**maintenance** :
A test that is in fact an explicit maintainer operation, hosted by the test runner because it composes existing library modules and benefits from assertions, such as regenerating a derived lexicon, mining a collected corpus or refreshing a calibrated artifact. It may read local maintainer data or write versioned or runtime artifacts, so it never runs during an ordinary `dotnet test`.
_Avoid_ : gesture, tool test.

**Gesture** :
In ordinary use, a single act someone performs by hand. A maintainer operation hosted by the test runner is a maintenance test, and Deckle already uses the word for touchpad gestures and for the operations the MCP host exposes.

**Tool test** :
A test of a tool, or a tool that happens to live among the tests. A maintainer operation run from the test runner is a maintenance test.

## Key distinction between integration and system

The boundary between `integration` and `system` depends on how heavy the dependency is and whether it can be substituted. The `Deckle.Audio.MicrophoneCapture.Probe` function, which queries the audio device for its capabilities, is `integration` when a fake audio source stands behind the WASAPI seam. A test that records three seconds of real voice in a full loop is `interactive`. A test that drives Whisper on a wav stored in the test repo is `system`.

### Example conversation

> - Le bug d'hier sur le clipboard Win32, on le couvre comment ?
> - Test de regression. `OpenClipboard` retournait `false` quand un autre process tenait la session. La fix réessaie trois fois. Le test simule trois échecs puis un succès, et vérifie qu'on a bien copié.
> - D'accord. Et pour vérifier qu'on émet le bon `ClipboardCopied` à la fin ?
> - C'est de l'observability. Un `TestEventListener` accroché à `DeckleWhispSource`, et on assert sur la séquence et sur le payload.
> - Et le micro maintenant ? Je voudrais tester qu'on ne plante pas quand il n'y en a pas.
> - Integration. On simule un device qui retourne « no input » et on vérifie le chemin d'erreur. Un test interactive prendrait un vrai micro débranché, utile mais à la main.
