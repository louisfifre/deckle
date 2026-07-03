using System.IO;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Reads the typed-text corpus back off an autocorrect.text.jsonl line: the payload
// round-trips into a SentenceRecord, and malformed or partial lines are skipped
// rather than fatal. No disk beyond a temp file for the streaming case.
[Trait("Category", "unit")]
public sealed class CorpusReaderTests
{
    private const string WellFormed =
        """{"timestamp":"2026-07-03T14:30:45.1+02:00","kind":"autocorrect_text","session":"s1","payload":{"process":"WINWORD.EXE","typed":"je vais a la banque.","final":"je vais à la banque.","history":"#2=a»sentence:à"}}""";

    [Fact]
    public void ParsesThePayloadIntoARecord()
    {
        Assert.True(CorpusReader.TryParse(WellFormed, out SentenceCorpus.SentenceRecord record));

        Assert.Equal("je vais a la banque.", record.Typed);
        Assert.Equal("je vais à la banque.", record.Final);
        Assert.Equal("#2=a»sentence:à", record.History);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"kind":"autocorrect_text"}""")]                       // no payload
    [InlineData("""{"payload":{"process":"x"}}""")]                        // neither typed nor final
    public void SkipsBlankMalformedAndPayloadlessLines(string line) =>
        Assert.False(CorpusReader.TryParse(line, out _));

    [Fact]
    public void MissingHistoryDefaultsToEmpty()
    {
        const string noHistory =
            """{"payload":{"typed":"bonjour monde.","final":"bonjour monde."}}""";

        Assert.True(CorpusReader.TryParse(noHistory, out SentenceCorpus.SentenceRecord record));
        Assert.Equal("", record.History);
    }

    [Fact]
    public void StreamsOnlyTheValidLinesOfAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckle-corpus-{System.Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, new[]
        {
            WellFormed,
            "",                                                            // blank
            "{ truncated tail from a crash",                               // partial line
            """{"payload":{"typed":"a.","final":"à."}}""",
        });

        try
        {
            var records = CorpusReader.Read(path).ToList();

            Assert.Equal(2, records.Count);
            Assert.Equal("je vais a la banque.", records[0].Typed);
            Assert.Equal("à.", records[1].Final);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
