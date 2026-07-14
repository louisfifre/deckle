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

    private static EventEntry Entry(string? formattedMessage) =>
        new(
            timestamp: new DateTimeOffset(2026, 7, 14, 12, 34, 56, 789, TimeSpan.Zero),
            provider: "Deckle-Vision",
            eventName: "CaptureStarted",
            level: EventLevel.Informational,
            keywords: EventKeywords.None,
            formattedMessage: formattedMessage,
            payload: new Dictionary<string, object?>());
}
