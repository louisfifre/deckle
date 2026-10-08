---
description: Notification catalogue, dispatcher and channels for user prompts.
type: agent-instructions
---

# AGENTS.md — Deckle.Notifications

## Caller contract

- A prompt returns the user's `NotificationResponse`, or null. Null means dropped (no channel could show it) or unanswered (shown, never answered).
- Null is a legitimate answer, for example ignoring an enrollment prompt. Every caller tolerates it.
- An unregistered descriptor is a programmer error and throws.

## The descriptor Id is a public contract

`NotificationDescriptor.Id` is `point.snake_case` and stable from day one. It keys the catalogue, routes the toast activation and may reach user preferences. Never rename it and never recycle a retired Id: a stale Id in a delivered toast or a stored preference would silently mis-route.

## String keys

Descriptor string keys resolve from the root resource map only: mirror them into Deckle.App's `Resources.resw` (see [Deckle.Catalog](../Deckle.Catalog/AGENTS.md)). A key left un-mirrored does not fail. The toast shows `[!key]` in DEBUG and blank text in RELEASE.

## Adding a consumer

`PlaygroundNotifications.cs` is the example. Two steps fail silently when skipped:

- Register the batch at boot with `dispatcher.Catalog.Register(...)` in `OnLaunched`, or every prompt throws `InvalidOperationException`.
- `NotificationDispatcher.Instance` is null before the boot wiring runs. Null-check it.

## Toast traps

- Subscribe `NotificationInvoked` before `AppNotificationManager.Register()`. Registered first, Windows treats every activation as a cold launch and spawns a new process.
- Windows silently drops toasts from an elevated process. The channel reports itself unavailable when elevated, so the dispatcher drops the prompt.
- Without an application icon registered for the unpackaged app, the toast shows a generic icon. The icon is an app-shell concern.
- `AppNotificationManager` raises no dismiss or expiry event. The channel settles an unclicked prompt itself: a finite `Expiration` on the toast plus a matching internal expiry that returns null. Without it the await and its pending entry leak for the process life.

## Scope

v1 has no user preferences, opt-out, throttling, extra channels or cold-activation handling. Add none without a decision.
