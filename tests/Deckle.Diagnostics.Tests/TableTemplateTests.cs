using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// The check the suites run on their table-row templates, pinned on literal
// templates so its verdict cannot drift with a provider.
[Trait("Category", "unit")]
public sealed class TableTemplateTests
{
    [Fact]
    public void AlignedColumnsHoldAcrossNarrowAndWideValues()
    {
        string? breach = TableTemplate.Breach(
            "state={0,-8} | rms_dbfs={1,6:F1} | utterance_ms={2,6}",
            ["Speech", -9.5, 0],
            ["Hangover", -120.0, 999999]);

        Assert.Null(breach);
    }

    [Fact]
    public void AnUnalignedColumnMovesTheSeparatorsAfterIt()
    {
        string? breach = TableTemplate.Breach(
            "state={0} | rms_dbfs={1:F1}",
            ["Speech", -9.5],
            ["Hangover", -9.5]);

        Assert.NotNull(breach);
        Assert.Contains("state=Hangover", breach!);
    }

    [Fact]
    public void AValueWiderThanItsColumnIsReported()
    {
        string? breach = TableTemplate.Breach(
            "index={0,3} | whisper_ms={1,5}",
            [0, 120],
            [1000, 120]);

        Assert.NotNull(breach);
    }

    [Fact]
    public void ATrailingFreeTextFieldDoesNotMoveAnySeparator()
    {
        // The last field has no separator after it, so a path or a message
        // of any length may close a row without breaking the table.
        string? breach = TableTemplate.Breach(
            "index={0,3} | ex_message={1}",
            [1, "short"],
            [12, "a much longer message"]);

        Assert.Null(breach);
    }

    [Fact]
    public void OneSampleRowIsNotEnoughToCompare()
    {
        Assert.NotNull(TableTemplate.Breach("index={0,3}", [1]));
    }
}
