---
name: deckle-settings-ux
description: User experience doctrine for the settings surfaces of the Deckle project (Windows .NET 10 / WinUI 3). Carries the information architecture principles (sensible defaults, progressive disclosure, settings vs commands, staged disclosure for conditional pages, immediate application without validate button, customization as cost) anchored on the substantiated sources Nielsen Norman Group and the official Microsoft doctrine. Triggers on phrases like deckle settings UX, deckle UX settings, organize settings deckle, settings page deckle, what do I expose deckle, prioritize settings deckle, progressive disclosure deckle, settings vs commands deckle, dynamic page deckle.
---

# Deckle — Experience doctrine for settings surfaces

## Role

Project-specific skill that answers one question: **what do we expose to the user in settings surfaces, and how do we organize it**. Invoked before adding an exposed setting, reorganizing a page, deciding to hide or promote an option, or overhauling a surface that has accumulated complexity over time.

The doctrine applies to settings pages, the log window, the title bar, and any surface where the user configures or inspects the state of the system. It does not describe the visual rendering — visual consistency is carried by native Windows primitives. It describes what deserves to appear, where, and with what weight.

## Sensible defaults before customization

The vast majority of users never touch settings; those who do are a minority. The consequence is that **every exposed setting is a UX debt, not a feature**. The default arbitration is to choose the right default behavior, not to expose a knob. When a setting seems necessary, the first question is: can it be deduced or inferred automatically? If yes, the system does it and the user has nothing to choose. If not, we expose it, but we choose the default value as if it were the only one possible — the user should not need to modify it for the app to work well in their most common use case.

## Progressive disclosure at two levels maximum

The user must never have to descend more than two levels to reach a setting. Beyond that, the readability of the navigation collapses — it is an established rule that designs beyond two levels of progressive disclosure have low usability because users get lost backtracking.

Concretely, the first level is the top-level navigation that groups surfaces by theme. The second level is the disclosure inside a page — less frequent options live in a fold that opens on demand. Beyond that, the signal is that we should overhaul the information architecture, not add a third level. **Folds never nest.**

## Settings versus commands

A framing distinction often poorly respected. A **setting** is a persistent configuration that modifies the future behavior of the application. A **command** is an immediate action that acts on the current context.

The inflation of exposed settings often comes from the confusion between the two. "Export the logs" is a command, not a setting. "Run calibration" is a command, not a setting. "Reset this group of options" is a command, not a setting. Commands live in action buttons, in context menus, in dialogs — not in the list of exposed settings. Keeping this distinction clean considerably lightens configuration pages.

## Staged disclosure for conditional pages

When a setting only makes sense in a certain context (another setting enabled, a module configured, a device detected), it **is not displayed grayed out, it is not displayed**. The recognized pattern in information architecture of complex UIs says that options are only shown to the user when they are relevant to the current task or to the selected object.

A page that becomes hyper-dynamic with numerous interdependencies should be read as a signal of incorrect application of this doctrine — either the page mixes several concerns that would deserve to be separated, or the conditional tree is too dense to remain understandable. Overhauling such a page goes through: identifying the independent axes (one setting governs only one other), grouping options that co-vary with the same axis, and making them appear or disappear conditionally rather than graying them out.

## Immediate application without validate button

Microsoft norm on Windows: when the user modifies a setting, the application immediately reflects the change. No "Save" button, no "Apply" button, no "OK" button that validates a session of modifications.

The implication on the code side is auto-save persistence and immediate visual feedback. The implication on the UX side is that a lightweight undo mechanism is needed (at least per session, ideally per setting) rather than a heavy explicit validation mechanism. The rare cases that deserve an explicit validation (settings whose error has a heavy cost, or actions that have an external consequence) shift into the commands category — not into the settings category.

## Customization has a cost

Distinction laid out by NN/g sources: **customization** gives control to the user ("choose the theme"), **personalization** executes it for them ("we detected your system theme"). Customization has a real usability cost — users frequently encounter difficulties when they try to accomplish customization activities.

This is an additional argument to reduce the surface of exposed settings and prefer automatic system adaptations when possible. The default value "follow the system" is almost always the right one for options that touch appearance (theme, language, contrast).

## Semantic distinction of options

For two options that appear visually similar (for example two toggles side by side, two list items), explicitly clarify what differentiates them. Each option has a short label and a description that specifies what it does, not what it is. Labels are short and factual; the description carries the expected effect, not the technical justification.

## Go fetch the material, do not expose everything

Before overhauling a surface, do a **factual inventory** of what is exposed today and what is persisted but hidden. The overhaul consists as much of **removing** as of organizing. Many options accumulated over time no longer have a reason to exist or can become default behaviors. The overhaul is also the opportunity to identify commands disguised as settings and to put them back in the right category.

## Log surface and diagnostic window

The log window and any diagnostic surface follow the same principles — sensible defaults, controlled progressive disclosure, distinction between settings (persistent filters) and commands (live search, copy, export). A catalog or schema view is consulted, not configured — it therefore falls more under diagnostics than under settings and has its place in the log window rather than in the settings.

## Pointers

- **`save-context`** — when a non-trivial UX decision is taken, route it through the cascade for a durable trace.
- The canonical sources (NN/g on progressive disclosure and customization, Microsoft doctrine on Windows app settings) are to be consulted outside of this skill when a case exceeds the perimeter covered here.
