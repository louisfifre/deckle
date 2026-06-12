using System.Collections.Generic;

namespace Deckle.Notifications;

// A notification's stable, declarative shape. Owning modules declare their
// descriptors once and register them in the NotificationCatalog at boot; the
// channel resolves the .resw keys and severity at show time. A descriptor
// carries no state and no behavior — it is the contract between a module that
// wants to ask the user something and the dispatcher that delivers it.

public enum NotificationSeverity
{
    Info,
    Warning,
    Error,
}

// The kind of surface a notification prefers. Today only the toast exists;
// InfoBar / dialog / HUD channels are deliberately deferred (see CLAUDE.md).
public enum NotificationChannel
{
    Toast,
}

public sealed record NotificationDescriptor(
    // Stable, point.snake_case identity (e.g. "playground.test_prompt"). Public
    // contract from day one: it keys the catalogue, threads through the toast
    // activation, and may appear in user preferences later — never rename or
    // recycle it.
    string Id,
    string Category,
    // .resw key resolved via Deckle.Catalog.Loc by the channel.
    string TitleKey,
    // .resw key; composite-format placeholders allowed, resolved with Loc.Format
    // when body arguments are supplied.
    string BodyKey,
    NotificationSeverity Severity,
    // A preference, not a command: the dispatcher routes to the matching channel
    // when available, and drops (returns null) when it is not.
    NotificationChannel Channel,
    IReadOnlyList<NotificationAction> Actions,
    NotificationTextInput? TextInput = null);
