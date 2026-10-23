using System.Diagnostics.Tracing;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "regression")]
public sealed class JsonlLineSerializerRegressionTests
{
    private static JsonElement Serialize(object? value, string? message = "sample")
    {
        var entry = new EventEntry(
            timestamp:        DateTimeOffset.UtcNow,
            provider:         "Deckle-SerializerTest",
            eventName:        "Sample",
            level:            EventLevel.Informational,
            keywords:         EventKeywords.None,
            kind:             ObservationKind.Operational,
            formattedMessage: message,
            payload:          new Dictionary<string, object?> { ["value"] = value });
        ReadOnlyMemory<byte> line = new JsonlLineSerializer().Serialize(entry, "log", JsonlSchema.SelfDescribing);
        return JsonDocument.Parse(Encoding.UTF8.GetString(line.Span)).RootElement.Clone();
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void A_non_finite_double_is_written_as_text_instead_of_losing_the_row(double value, string expected)
    {
        JsonElement root = Serialize(value);

        Assert.Equal(expected, root.GetProperty("payload").GetProperty("value").GetString());
    }

    [Fact]
    public void A_non_finite_float_is_written_as_text_instead_of_losing_the_row()
    {
        JsonElement root = Serialize(float.NaN);

        Assert.Equal("NaN", root.GetProperty("payload").GetProperty("value").GetString());
    }

    [Fact]
    public void A_finite_double_stays_a_json_number()
    {
        JsonElement root = Serialize(1.5d);

        Assert.Equal(1.5d, root.GetProperty("payload").GetProperty("value").GetDouble());
    }
}
