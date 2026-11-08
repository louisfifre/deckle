---
description: Deckle's own animations follow a Deckle setting, Same as Windows by default, because the Windows switch alone cannot tell a need for reduced motion from a dislike of Windows's own animations.
---

# ADR-0009 — Give Deckle its own animation setting, defaulting to Windows

**Status** — accepted 2026-11-07, amended 2026-11-08 (Reduced shows no processing outline; a one-time notification offers the setting)

## Context

Deckle draws its own motion: the HUD outline that answers the microphone, then turns while a take is transcribed and rewritten, the reveal in the clock digits, the slides and fades of the HUD windows. Commit 14ae67cc made all of it follow the Windows "Animation effects" preference, the switch under Settings ▸ Accessibility ▸ Visual effects, and removed a Deckle switch that no card exposed.

The Windows switch carries two different wishes. Some people turn it off because motion hurts them: it breaks their focus, or brings too many sensations at once, and a coloured, turning outline is the kind of motion that does it. Others turn it off because Windows's own animations are slow and badly made, and want them gone for speed, not for their eyes. Deckle cannot tell one from the other. Followed to the letter, the switch took the HUD's only activity signal away from the second group: once the clock stops, the turning outline is what says that Deckle is still working. Ignored, it would hurt the first group.

WCAG 2.2.2 (Pause, Stop, Hide) asks that motion which starts by itself and lasts more than five seconds can be stopped; a long transcription lasts longer. Applying a web criterion to a desktop overlay is our reading, not a requirement anyone set.

## Decision

Deckle MUST read one setting, `Appearance.Animations`, for every animation it draws itself, through `Deckle.Hud.AnimationPreference`. The setting has three values:

- **Same as Windows** (`System`, the default): the Windows preference decides, so a person who needs reduced motion gets it without finding a Deckle setting.
- **On**: Deckle animates whatever Windows says.
- **Reduced**: Deckle does not animate, whatever Windows says.

The setting sits on the General page, beside the theme, because it acts on all of Deckle and not on one module. A person whose Windows animations are off, and who never chose for Deckle, is offered Deckle's own animations once, by a notification at launch: the setup wizard cannot carry the choice, since its first process cannot write the settings yet and its second shows no page when nothing is left to download. Under Reduced, nothing that turns is shown still: Transcribing and Rewriting show no outline and no material in the digits, only the stopped clock, because a turning material frozen mid-turn reads as broken. Live measurements keep being written, only without the glide between samples: the Recording outline still answers the microphone, and the pause countdown still runs.

Animations that WinUI draws itself (page transitions, control states) keep following Windows: Deckle does not drive them.

The HUD page that will take over the Playground's HUD controls SHOULD carry a "Glowing outline" switch, on by default, that removes the outline in every state for a person who finds it intrusive even still. It is not built here: that page does not exist yet.

## Consequences

A person who turned Windows's animations off for speed gets Deckle's back with one choice, and nobody gets motion they did not ask for: the default is unchanged. Each new animation reads `AnimationPreference`, never `UISettings.AnimationsEnabled` directly; a direct read would bring back the bug (tests/Deckle.Hud.Tests/AnimationPreferenceBugNotes.md).

`AnimationPreference` lives in Deckle.Hud because all of today's readers do. A module outside the HUD that needs it will move it to a shared module first.

The setting is applied after the settings file is saved, within the save debounce, not on the same frame.

The outline itself is provisional: it reads as too loud, and live transcription text may become enough of an activity signal. This decision holds the gate, not the outline. It is to be reviewed when the outline is redesigned, or when the live text gives its own signal.

## Options considered

- **A. Follow Windows only** (14ae67cc) — no setting, but a person who dislikes Windows's animations loses Deckle's signals with them.
- **B. Always run the processing rotation** — keeps the signal for everyone, but forces a coloured, turning motion on the people the Windows switch protects.
- **C. Deckle setting, Same as Windows by default (retained)** — one more setting, for a default that stays accessible and a way out for both groups.
