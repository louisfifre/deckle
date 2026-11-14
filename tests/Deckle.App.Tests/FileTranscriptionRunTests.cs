using Xunit;

namespace Deckle.App.Tests;

// The file assistant's run: each file in turn, a failure that stops only its
// own file, and a rewrite that never costs the transcript.
[Trait("Category", "unit")]
public sealed class FileTranscriptionRunTests
{
    private static FileTranscriptionItem[] Items(params string[] paths)
        => [.. paths.Select(p => new FileTranscriptionItem(p))];

    [Fact]
    public async Task EachFileIsWrittenBesideItsAudioInOrder()
    {
        var written = new List<(string Text, string Audio)>();
        var run = new FileTranscriptionRun(
            path => Task.FromResult<string?>("text of " + path),
            rewrite: null,
            (text, audio) => { written.Add((text, audio)); return audio + ".txt"; });
        FileTranscriptionItem[] items = Items(@"C:\a.mp3", @"C:\b.mp3");

        await run.RunAsync(items);

        Assert.Equal([("text of C:\\a.mp3", @"C:\a.mp3"), ("text of C:\\b.mp3", @"C:\b.mp3")], written);
        Assert.All(items, i => Assert.Equal(FileTranscriptionState.Done, i.State));
        Assert.Equal(@"C:\b.mp3.txt", items[1].OutputPath);
    }

    [Fact]
    public async Task AFailedTranscriptionStopsOnlyItsFile()
    {
        var run = new FileTranscriptionRun(
            path => Task.FromResult(path.Contains("bad") ? null : "text"),
            rewrite: null,
            (_, audio) => audio + ".txt");
        FileTranscriptionItem[] items = Items(@"C:\bad.mp3", @"C:\good.mp3");

        await run.RunAsync(items);

        Assert.Equal(FileTranscriptionState.TranscriptionFailed, items[0].State);
        Assert.Null(items[0].OutputPath);
        Assert.Equal(FileTranscriptionState.Done, items[1].State);
    }

    [Fact]
    public async Task TheRewrittenTextIsWhatGetsWritten()
    {
        string? written = null;
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"),
            text => text.ToUpperInvariant(),
            (text, audio) => { written = text; return audio + ".txt"; });
        FileTranscriptionItem[] items = Items(@"C:\a.mp3");

        await run.RunAsync(items);

        Assert.Equal("RAW", written);
        Assert.False(items[0].RewriteFailed);
    }

    // Hours of transcription are not lost to a provider that answers nothing.
    [Fact]
    public async Task AnEmptyRewriteSavesTheRawTranscript()
    {
        string? written = null;
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"),
            _ => "",
            (text, audio) => { written = text; return audio + ".txt"; });
        FileTranscriptionItem[] items = Items(@"C:\a.mp3");

        await run.RunAsync(items);

        Assert.Equal("raw", written);
        Assert.True(items[0].RewriteFailed);
        Assert.Equal(FileTranscriptionState.Done, items[0].State);
    }

    [Fact]
    public async Task AFailedWriteMarksTheFileAndTheRunGoesOn()
    {
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("text"),
            rewrite: null,
            (_, audio) => audio.Contains("locked") ? throw new UnauthorizedAccessException() : audio + ".txt");
        FileTranscriptionItem[] items = Items(@"C:\locked.mp3", @"C:\open.mp3");

        await run.RunAsync(items);

        Assert.Equal(FileTranscriptionState.WriteFailed, items[0].State);
        Assert.Equal(FileTranscriptionState.Done, items[1].State);
    }
}
