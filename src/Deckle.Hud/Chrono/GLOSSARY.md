---
name: glossary-deckle-hud-chrono
description: "Chrono HUD element vocabulary. It names the face's eight elements and the accent twins, and tells animated digits, at-rest digits and dots apart."
---

# Deckle.Hud, Chrono glossary

Shared vocabulary for the chrono face, `HudChrono`. The face is the `MM.SS.cc` clock inside the HUD card. This file fixes the terms that Louis and the agents use.

## Elements, the chrono face

The face shows `MM.SS.cc` as eight separate elements, from left to right.

- **Min1** and **Min2** are the two minute digits.
- **DotA** is the separator between minutes and seconds (`.`).
- **Sec1** and **Sec2** are the two seconds digits.
- **DotB** is the separator between seconds and centiseconds (`.`).
- **Cs1** and **Cs2** are the two centisecond digits, the hundredths of a second.

**Accent twin** :
An overlay that each digit carries in its own cell, named after it, such as `Min1Accent` or `Sec2Accent`. It holds the exact Windows accent colour and stays invisible until the digit flashes in that colour.

**Animated digit** :
A digit that advanced during the take, while recording. The difference between animated and at-rest digits exists only during Recording.

**At-rest digit** :
A digit that is not advancing at this moment. Outside Recording, every digit is at rest.

**Dot** :
A separator of the face, DotA or DotB. It is never animated and always takes the at-rest tone.
