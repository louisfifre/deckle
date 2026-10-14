---
name: glossary-deckle-settings
description: "Settings vocabulary. It defines the exposable, the family of adjustable values held in the code, of which a setting is the persisted and surfaced kind. Read before adding a setting or naming an adjustable value."
---

# Deckle.Settings, Glossary

Vocabulary of the settings surface. It names the family of adjustable values that the code holds, so that a setting is told apart from the wider family it belongs to.

## The exposable

**Exposable** :
An adjustable value held in the code, whether or not an interface shows it today. Its kinds are the persisted setting, the tuning constant, the default and the Playground parameter held in memory. A *setting* is the narrowest kind, an exposable that is persisted and surfaced.
_Avoid_ : setting (only the persisted, surfaced subset), parameter (too generic, it names any function argument).

**Setting** :
In ordinary use, any value a user can change in an application. Deckle keeps the word for an exposable that is persisted and surfaced, and calls the wider family exposables.

**Parameter** :
In ordinary use, any input that a function or a model receives. It is too generic to name a value someone could act on, which Deckle calls an exposable.
