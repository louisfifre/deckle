using System.IO;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Reads the typed-text corpus back off an autocorrect.text.jsonl line: the payload
// round-trips into a SentenceRecord, closure and timing default when absent, the
// history PROPERTY's presence is surfaced apart from its value, and malformed or
// partial lines are skipped rather than fatal.
public sealed class CorpusReaderTests
{
    private const string WellFormed =
        """{"timestamp":"2026-07-03T14:30:45.1+02:00","kind":"autocorrect_text","session":"s1","payload":{"process":"WINWORD.EXE","typed":"je vais a la banque.","final":"je vais à la banque.","history":"#2=a»sentence:à","closure":"sentence","timing":"0,340,1220,90,110"}}""";

    [Fact]
    [Trait("Category", "unit")]
    public void ParsesThePayloadIntoARecord()
    {
        Assert.True(CorpusReader.TryParse(WellFormed, out CorpusEntry entry));

        Assert.Equal("je vais a la banque.", entry.Record.Typed);
        Assert.Equal("je vais à la banque.", entry.Record.Final);
        Assert.Equal("#2=a»sentence:à", entry.Record.History);
        Assert.Equal("sentence", entry.Record.Closure);
        Assert.Equal("0,340,1220,90,110", entry.Record.Timing);
        Assert.True(entry.HistoryPresent);
    }

    [Theory]
    [Trait("Category", "unit")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"kind":"autocorrect_text"}""")]
    [InlineData("""{"payload":{"process":"x"}}""")]                        // neither typed nor final
    public void SkipsBlankMalformedAndPayloadlessLines(string line) =>
        Assert.False(CorpusReader.TryParse(line, out _));

    [Fact]
    [Trait("Category", "unit")]
    public void CarriesTheClosureThatEndedTheRun()
    {
        const string enterClosed =
            """{"payload":{"typed":"bonjour","final":"bonjour","history":"","closure":"enter","timing":"0"}}""";

        Assert.True(CorpusReader.TryParse(enterClosed, out CorpusEntry entry));
        Assert.Equal("enter", entry.Record.Closure);
    }

    // A legacy line lacks history/closure/timing entirely. The reader flags that
    // the history PROPERTY was absent, which the alignment reads as « a writer that
    // predated the field », not « a sentence with no corrections ».
    [Fact]
    [Trait("Category", "unit")]
    public void LegacyLineDefaultsClosureAndFlagsHistoryAbsent()
    {
        const string legacy =
            """{"payload":{"typed":"mise a jour.","final":"mise à jour."}}""";

        Assert.True(CorpusReader.TryParse(legacy, out CorpusEntry entry));
        Assert.False(entry.HistoryPresent);
        Assert.Equal("", entry.Record.History);
        Assert.Equal("sentence", entry.Record.Closure);
        Assert.Equal("", entry.Record.Timing);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void EmptyHistoryPropertyStillCountsAsPresent()
    {
        const string emptyHistory =
            """{"payload":{"typed":"bonjour monde.","final":"bonjour monde.","history":""}}""";

        Assert.True(CorpusReader.TryParse(emptyHistory, out CorpusEntry entry));
        Assert.True(entry.HistoryPresent);
        Assert.Equal("", entry.Record.History);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void StreamsOnlyTheValidLinesOfAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckle-corpus-{System.Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, new[]
        {
            WellFormed,
            "",
            "{ truncated tail from a crash",
            """{"payload":{"typed":"a.","final":"à."}}""",
        });

        try
        {
            var entries = CorpusReader.Read(path).ToList();

            Assert.Equal(2, entries.Count);
            Assert.Equal("je vais a la banque.", entries[0].Record.Typed);
            Assert.Equal("à.", entries[1].Record.Final);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
