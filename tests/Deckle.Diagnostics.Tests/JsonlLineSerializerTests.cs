using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;
using System.Text.Json;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "observability")]
public sealed class JsonlLineSerializerTests
{
    private static EventEntry Entry(EventKeywords keywords) =>
        new(
            timestamp:        DateTimeOffset.UtcNow,
            provider:         "Deckle-SerializerTest",
            eventName:        "Sample",
            level:            EventLevel.Informational,
            keywords:         keywords,
            kind:             ObservationKind.Operational,
            formattedMessage: "sample",
            payload:          new Dictionary<string, object?> { ["n"] = 1 });

    private static JsonElement Serialize(EventKeywords keywords, JsonlSchema schema)
    {
        // The serializer reuses its buffer, so the line is parsed before the
        // next Serialize call.
        ReadOnlyMemory<byte> line = new JsonlLineSerializer().Serialize(Entry(keywords), "log", schema);
        return JsonDocument.Parse(Encoding.UTF8.GetString(line.Span)).RootElement.Clone();
    }

    [Fact]
    public void SelfDescribingNamesTheTransverseKeywordsInDeclarationOrder()
    {
        // Passed in reverse order on purpose: the output follows the enum, not the caller.
        JsonElement root = Serialize(
            (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
            JsonlSchema.SelfDescribing);

        Assert.Equal("Lifecycle, Capture", root.GetProperty("keywords").GetString());
    }

    [Fact]
    public void SelfDescribingWritesAnEmptyStringWhenNoTransverseBitIsSet()
    {
        JsonElement root = Serialize(EventKeywords.None, JsonlSchema.SelfDescribing);

        Assert.Equal(string.Empty, root.GetProperty("keywords").GetString());
    }

    [Fact]
    public void SelfDescribingIgnoresProviderLocalBits()
    {
        // Bit 10 is a provider-local keyword; only the transverse bit is named.
        JsonElement root = Serialize(
            (EventKeywords)((long)Keywords.Network | (1L << 10)),
            JsonlSchema.SelfDescribing);

        Assert.Equal("Network", root.GetProperty("keywords").GetString());
    }

    [Fact]
    public void SelfDescribingWritesAnEmptyStringWhenOnlyProviderLocalBitsAreSet()
    {
        JsonElement root = Serialize((EventKeywords)(1L << 10), JsonlSchema.SelfDescribing);

        Assert.Equal(string.Empty, root.GetProperty("keywords").GetString());
    }

    [Fact]
    public void PayloadOnlyHasNoKeywordsField()
    {
        JsonElement root = Serialize(
            (EventKeywords)(Keywords.Lifecycle | Keywords.Capture),
            JsonlSchema.PayloadOnly);

        Assert.False(root.TryGetProperty("keywords", out _));
    }
}
