---
description: Notification catalogue, dispatcher, and delivery channels — modules declare descriptors, the dispatcher routes a prompt to the matching channel.
type: agent-instructions
---

# CLAUDE.md — Deckle.Notifications

The subsystem that asks the user something and waits for an answer. Three parts: a **catalogue** of declarative `NotificationDescriptor`s, a **dispatcher** that routes a prompt to a channel, and the **channels** that deliver it. Owning modules declare their descriptors and register them in the catalogue at boot; the dispatcher is the composition root, wired once via `Initialize` and reached through `Instance`. The only channel today is the interactive Windows toast.

A prompt returns the user's `NotificationResponse`, or **null** for either of two no-answer cases: no channel could show it (none registered, or the channel is unavailable — *dropped*), or it was shown but never answered (the toast expired unseen — *unanswered*). The narrative tells the two apart; the caller contract collapses them to "no answer". Null is a legitimate answer — for an enrollment prompt, ignoring is a valid choice — so every caller tolerates it. An unregistered descriptor, by contrast, is a programmer error and throws.

## The descriptor Id is a public contract

`NotificationDescriptor.Id` is `point.snake_case`, stable from day one. It keys the catalogue, threads through the toast activation as the routing token, and may surface in user preferences later. Never rename it, never recycle a retired Id for a different notification — a stale Id in a delivered toast or a persisted preference would silently mis-route.

## Toast traps

Four platform facts, each capable of a silent failure:

- **Handler before Register.** `NotificationInvoked` must be subscribed *before* `AppNotificationManager.Register()`. Register first and Windows treats every activation as a cold launch, spawning a fresh process to deliver it instead of raising the event in the running one. Proven by `spikes/InteractiveToast/Program.cs`.
- **Elevated process = dead toasts.** Windows silently drops toasts raised from an elevated app. The channel reports itself unavailable when the process is elevated, so the dispatcher drops rather than showing a toast no one will ever see.
- **Exe icon required.** Without an application icon registered for the unpackaged app, the toast shows a generic placeholder icon. The icon is an app-shell concern, surfaced here so it is not forgotten when wiring the channel into the host.
- **No dismiss event.** `AppNotificationManager` raises `NotificationInvoked` *only* on user interaction — there is no dismiss, expiry, or timeout event (unlike legacy UWP `ToastNotification.Dismissed`). A prompt nobody clicks must be settled by the channel itself: a finite `Expiration` on the toast plus an apparied internal expiry that settles the prompt as unanswered (null) on the same horizon. Without it the await and its pending entry leak for process life.

## Deliberately deferred

Out of scope for v1, by intent — do not add without a decision: user preferences and per-notification overrides, a `neverShowAgain` opt-out, throttling and coalescence of bursts, additional channels (InfoBar, dialog, HUD), a source generator for descriptors, cold-activation handling after the process has exited (a toast acted on from the Notification Center once Deckle is gone is currently an orphan), and fallback routing when the preferred channel is unavailable (notifications disabled in Windows → route to a future HUD/InfoBar channel; the dispatcher's drop-with-reason is the hook).
