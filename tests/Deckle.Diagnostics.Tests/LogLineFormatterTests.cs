using System.Diagnostics.Tracing;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "unit")]
public sealed class LogLineFormatterTests
{
    [Fact]
    public void Parts_recompose_the_canonical_line()
    {
        var entry = Entry(formattedMessage: "Capture started");

        LogLineParts parts = LogLineFormatter.GetParts(entry);

        Assert.Equal("12:34:56.789", parts.Timestamp);
        Assert.Equal("Vision", parts.Source);
        Assert.Equal("Capture started", parts.Message);
        Assert.Equal("12:34:56.789 [Vision] Capture started", parts.Text);
        Assert.Equal(parts.Text, LogLineFormatter.Format(entry));
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

        Assert.Equal("Notifications Provider With A Long Name", parts.Source);
        Assert.Contains(parts.Source, parts.Text);
        Assert.EndsWith(message, parts.Text);
    }

    [Theory]
    [InlineData("Deckle", "App")]
    [InlineData("Deckle-App", "App")]
    [InlineData("Deckle-Whisp", "Dictation")]
    [InlineData("Deckle-Llm", "LLM")]
    [InlineData("Deckle-AnytypeMcp", "Anytype MCP")]
    [InlineData("Deckle-PrecisionScroll", "Precision Scroll")]
    [InlineData("Deckle-Lighting.Ambient", "Lighting Ambient")]
    [InlineData("Deckle-Audio", "Audio")]
    [InlineData("Deckle-Vad", "VAD")]
    [InlineData("Deckle-SettingsUx", "Settings UX")]
    [InlineData("Deckle-", "Deckle-")]
    [InlineData("Microsoft-Windows-DotNETRuntime", "Microsoft-Windows-DotNETRuntime")]
    public void Display_name_reads_as_a_module_name(string provider, string expected)
    {
        Assert.Equal(expected, LogLineFormatter.DisplayName(provider));
    }

    [Theory]
    [InlineData("Deckle", "APP")]
    [InlineData("Deckle-App", "APP")]
    [InlineData("Deckle-Whisp", "WHISP")]
    [InlineData("Deckle-PrecisionScroll", "PRECISIONSCROLL")]
    [InlineData("Microsoft-Windows-DotNETRuntime", "MICROSOFT-WINDOWS-DOTNETRUNTIME")]
    public void Jsonl_source_token_keeps_its_machine_vocabulary(string provider, string expected)
    {
        Assert.Equal(expected, LogLineFormatter.MapSource(provider));
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
