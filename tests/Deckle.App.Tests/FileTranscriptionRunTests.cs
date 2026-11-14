using Xunit;

namespace Deckle.App.Tests;

// The run of the window's file list: each file in turn, a failure that stops
// only its own file, and a rewrite that never costs the transcript.
[Trait("Category", "unit")]
public sealed class FileTranscriptionRunTests
{
    private static FileTranscriptionItem[] Items(params string[] paths)
        => [.. paths.Select(p => new FileTranscriptionItem(p))];

    // A disk in memory: write creates "<audio>.txt", overwrite replaces it.
    private sealed class Disk
    {
        public Dictionary<string, string> Files { get; } = [];

        public string Write(string text, string audio)
        {
            string path = audio + ".txt";
            Files[path] = text;
            return path;
        }

        public void Overwrite(string path, string text) => Files[path] = text;
    }

    [Fact]
    public async Task EachFileIsWrittenBesideItsAudio()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            path => Task.FromResult<string?>("text of " + path), null, disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3", @"C:\b.mp3");

        await run.RunAsync(items);

        Assert.Equal(@"text of C:\a.mp3", disk.Files[@"C:\a.mp3.txt"]);
        Assert.Equal(@"text of C:\b.mp3", disk.Files[@"C:\b.mp3.txt"]);
        Assert.All(items, i => Assert.Equal(FileTranscriptionState.Done, i.State));
        Assert.Equal(@"C:\b.mp3.txt", items[1].OutputPath);
    }

    [Fact]
    public async Task AFailedTranscriptionStopsOnlyItsFile()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            path => Task.FromResult(path.Contains("bad") ? null : "text"), null, disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\bad.mp3", @"C:\good.mp3");

        await run.RunAsync(items);

        Assert.Equal(FileTranscriptionState.TranscriptionFailed, items[0].State);
        Assert.Null(items[0].OutputPath);
        Assert.Equal(FileTranscriptionState.Done, items[1].State);
    }

    // The review of 2026-11-14: an exception left its file spinning and the
    // files after it waiting forever.
    [Fact]
    public async Task ATranscriptionThatThrowsStopsOnlyItsFile()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            path => path.Contains("bad") ? throw new InvalidOperationException() : Task.FromResult<string?>("text"),
            null, disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\bad.mp3", @"C:\good.mp3");

        await run.RunAsync(items);

        Assert.Equal(FileTranscriptionState.TranscriptionFailed, items[0].State);
        Assert.Equal(FileTranscriptionState.Done, items[1].State);
    }

    [Fact]
    public async Task TheRewriteReplacesTheTranscriptOnDisk()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"), text => text.ToUpperInvariant(), disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3");

        await run.RunAsync(items);

        Assert.Equal("RAW", disk.Files[@"C:\a.mp3.txt"]);
        Assert.False(items[0].RewriteFailed);
    }

    // The raw transcript is on disk before the rewrite runs, so a Deckle that
    // quits during a long rewrite keeps it.
    [Fact]
    public async Task TheTranscriptIsOnDiskWhileTheRewriteRuns()
    {
        var disk = new Disk();
        string? onDiskDuringRewrite = null;
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"),
            text => { onDiskDuringRewrite = disk.Files.GetValueOrDefault(@"C:\a.mp3.txt"); return "done"; },
            disk.Write, disk.Overwrite);

        await run.RunAsync(Items(@"C:\a.mp3"));

        Assert.Equal("raw", onDiskDuringRewrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEmptyOrThrowingRewriteKeepsTheRawTranscript(bool throws)
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"),
            _ => throws ? throw new HttpRequestException() : "",
            disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3", @"C:\b.mp3");

        await run.RunAsync(items);

        Assert.Equal("raw", disk.Files[@"C:\a.mp3.txt"]);
        Assert.True(items[0].RewriteFailed);
        Assert.All(items, i => Assert.Equal(FileTranscriptionState.Done, i.State));
    }

    [Fact]
    public async Task AFailedWriteMarksTheFileAndTheRunGoesOn()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("text"),
            null,
            (text, audio) => audio.Contains("locked") ? throw new UnauthorizedAccessException() : disk.Write(text, audio),
            disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\locked.mp3", @"C:\open.mp3");

        await run.RunAsync(items);

        Assert.Equal(FileTranscriptionState.WriteFailed, items[0].State);
        Assert.Equal(FileTranscriptionState.Done, items[1].State);
    }

    // The line shows what the .txt holds: the transcript, then its rewrite.
    [Fact]
    public async Task EachFileHoldsTheTextOfItsTxt()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"), text => text.ToUpperInvariant(), disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3");

        await run.RunAsync(items);

        Assert.Equal(disk.Files[@"C:\a.mp3.txt"], items[0].Text);
    }

    // A three-hour transcript never reaches a list line whole.
    [Fact]
    public void ALongTextIsCutForItsLine()
    {
        var item = new FileTranscriptionItem(@"C:\a.mp3") { Text = new string('a', 100_000) };

        Assert.True(item.HasText);
        Assert.True(item.Preview.Length < 1_000);
        Assert.StartsWith(item.Preview, item.Text);
    }

    private static async Task<(FileTranscriptionItem[] Items, Disk Disk)> TranscribedAsync(params string[] paths)
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            path => Task.FromResult(path.Contains("bad") ? null : "raw of " + path), null, disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(paths);
        await run.RunAsync(items);
        return (items, disk);
    }

    [Fact]
    public async Task RewriteAllReplacesEverySavedFile()
    {
        var (items, disk) = await TranscribedAsync(@"C:\a.mp3", @"C:\b.mp3");

        await FileTranscriptionRun.RewriteAllAsync(items, text => text.ToUpperInvariant(), disk.Overwrite);

        Assert.Equal(@"RAW OF C:\A.MP3", disk.Files[@"C:\a.mp3.txt"]);
        Assert.Equal(@"RAW OF C:\B.MP3", items[1].Text);
        Assert.All(items, i => Assert.Equal(FileTranscriptionState.Done, i.State));
    }

    [Fact]
    public async Task RewriteAllLeavesAFailedFileAlone()
    {
        var (items, disk) = await TranscribedAsync(@"C:\bad.mp3", @"C:\good.mp3");
        var rewritten = new List<string>();

        await FileTranscriptionRun.RewriteAllAsync(items, text => { rewritten.Add(text); return "new"; }, disk.Overwrite);

        Assert.Equal([@"raw of C:\good.mp3"], rewritten);
        Assert.Equal(FileTranscriptionState.TranscriptionFailed, items[0].State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RewriteAllThatFailsKeepsTheText(bool throws)
    {
        var (items, disk) = await TranscribedAsync(@"C:\a.mp3", @"C:\b.mp3");

        await FileTranscriptionRun.RewriteAllAsync(
            items, _ => throws ? throw new HttpRequestException() : "", disk.Overwrite);

        Assert.Equal(@"raw of C:\a.mp3", disk.Files[@"C:\a.mp3.txt"]);
        Assert.Equal(@"raw of C:\a.mp3", items[0].Text);
        Assert.All(items, i => Assert.True(i.RewriteFailed));
        Assert.All(items, i => Assert.Equal(FileTranscriptionState.Done, i.State));
    }

    // A second Rewrite all that succeeds clears the mark of a first that failed.
    [Fact]
    public async Task ASuccessfulRewriteClearsAnEarlierFailure()
    {
        var (items, disk) = await TranscribedAsync(@"C:\a.mp3");
        await FileTranscriptionRun.RewriteAllAsync(items, _ => "", disk.Overwrite);

        await FileTranscriptionRun.RewriteAllAsync(items, _ => "fine", disk.Overwrite);

        Assert.False(items[0].RewriteFailed);
        Assert.Equal("fine", disk.Files[@"C:\a.mp3.txt"]);
    }
}
