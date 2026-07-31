using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public class TranscriptFileWriterTests
{
    private const string Dir = @"C:\out";

    private static string InDir(string name) => Path.Combine(Dir, name);

    [Fact]
    public void NoCollisionKeepsThePlainName()
    {
        var taken = new HashSet<string>();

        string target = TranscriptFileWriter.ResolveTargetPath("meeting", Dir, taken.Contains);

        Assert.Equal(InDir("meeting.txt"), target);
    }

    [Fact]
    public void OneCollisionAppendsTwo()
    {
        var taken = new HashSet<string> { InDir("meeting.txt") };

        string target = TranscriptFileWriter.ResolveTargetPath("meeting", Dir, taken.Contains);

        Assert.Equal(InDir("meeting (2).txt"), target);
    }

    [Fact]
    public void ChainOfCollisionsAppendsThree()
    {
        var taken = new HashSet<string> { InDir("meeting.txt"), InDir("meeting (2).txt") };

        string target = TranscriptFileWriter.ResolveTargetPath("meeting", Dir, taken.Contains);

        Assert.Equal(InDir("meeting (3).txt"), target);
    }

    [Fact]
    public void NameAlreadyContainingSuffixIsKeptVerbatimWhenFree()
    {
        // A base name that already ends in " (2)" gets no special treatment: its
        // plain ".txt" is free, so it is kept as-is rather than re-parsed.
        var taken = new HashSet<string>();

        string target = TranscriptFileWriter.ResolveTargetPath("meeting (2)", Dir, taken.Contains);

        Assert.Equal(InDir("meeting (2).txt"), target);
    }

}
