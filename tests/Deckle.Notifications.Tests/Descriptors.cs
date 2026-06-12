using System;
using System.Collections.Generic;
using Deckle.Notifications;

namespace Deckle.Notifications.Tests;

// Small builder so each test states only the fields it cares about. The .resw
// keys and category are placeholders — the dispatcher and catalogue never
// resolve them (that is the channel's job), so a stable dummy keeps the tests
// about routing and lifecycle, not localization.
internal static class Descriptors
{
    public static NotificationDescriptor Make(
        string id,
        NotificationChannel channel = NotificationChannel.Toast,
        NotificationSeverity severity = NotificationSeverity.Info)
        => new(
            Id: id,
            Category: "test",
            TitleKey: "Test_Title",
            BodyKey: "Test_Body",
            Severity: severity,
            Channel: channel,
            Actions: Array.Empty<NotificationAction>(),
            TextInput: null);
}
