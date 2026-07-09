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

    [Fact]
    public void FreshSettingsCarryTheDesktopSentinel()
    {
        // The empty string is the load-bearing sentinel: the card's reset rewrites
        // it (never a resolved Desktop literal) and the resolver below expands it
        // at use time. A changed default would silently repoint every fresh install.
        Assert.Equal(string.Empty, new TranscriptionSettings().FileTranscriptionOutputDirectory);
    }

    [Fact]
    public void BlankOutputDirectoryResolvesToDesktop()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Assert.Equal(desktop, TranscriptionSettingsService.ResolveFileTranscriptionOutputDirectory(""));
        Assert.Equal(desktop, TranscriptionSettingsService.ResolveFileTranscriptionOutputDirectory("   "));
    }

    [Fact]
    public void ConfiguredOutputDirectoryIsReturnedVerbatim()
    {
        string configured = @"D:\Transcripts";

        string resolved = TranscriptionSettingsService.ResolveFileTranscriptionOutputDirectory(configured);

        Assert.Equal(configured, resolved);
    }
}
