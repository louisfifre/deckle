using System.Diagnostics.Tracing;
using Xunit;

namespace Deckle.Diagnostics.Tests;

public sealed class LogLineFormatterTests
{
    [Fact]
    public void Parts_recompose_the_canonical_line()
    {
        var entry = Entry(formattedMessage: "Capture started");

        LogLineParts parts = LogLineFormatter.GetParts(entry);

        Assert.Equal("12:34:56.789", parts.Timestamp);
        Assert.Equal("VISION", parts.Source);
        Assert.Equal("Capture started", parts.Message);
        Assert.Equal(LogLineFormatter.Format(entry), parts.Text);
    }

    [Fact]
    public void Event_name_is_used_when_no_message_was_formatted()
    {
        var entry = Entry(formattedMessage: null);

        Assert.Equal("CaptureStarted", LogLineFormatter.GetParts(entry).Message);
    }

    [Fact]
    public void Canonical_text_keeps_long_source_and_message_in_full()
    {
        string source = "Deckle-Notifications-Provider-With-A-Long-Name";
        string message = new('m', 20_000);
        var entry = new EventEntry(
            timestamp: new DateTimeOffset(2026, 7, 14, 12, 34, 56, 789, TimeSpan.Zero),
            provider: source,
            eventName: "LongEntry",
            level: EventLevel.Informational,
            keywords: EventKeywords.None,
            kind: ObservationKind.Operational,
            formattedMessage: message,
            payload: new Dictionary<string, object?>());

        LogLineParts parts = LogLineFormatter.GetParts(entry);

        Assert.Equal("NOTIFICATIONS-PROVIDER-WITH-A-LONG-NAME", parts.Source);
        Assert.Contains(parts.Source, parts.Text);
        Assert.EndsWith(message, parts.Text);
    }

    private static EventEntry Entry(string? formattedMessage) =>
        new(
            timestamp: new DateTimeOffset(2026, 7, 14, 12, 34, 56, 789, TimeSpan.Zero),
            provider: "Deckle-Vision",
            eventName: "CaptureStarted",
            level: EventLevel.Informational,
            keywords: EventKeywords.None,
            kind: ObservationKind.Operational,
            formattedMessage: formattedMessage,
            payload: new Dictionary<string, object?>());
}
