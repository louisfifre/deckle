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

        await run.RunAsync(items, TestContext.Current.CancellationToken);

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

        await run.RunAsync(items, TestContext.Current.CancellationToken);

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

        await run.RunAsync(items, TestContext.Current.CancellationToken);

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

        await run.RunAsync(items, TestContext.Current.CancellationToken);

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

        await run.RunAsync(Items(@"C:\a.mp3"), TestContext.Current.CancellationToken);

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

        await run.RunAsync(items, TestContext.Current.CancellationToken);

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

        await run.RunAsync(items, TestContext.Current.CancellationToken);

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

        await run.RunAsync(items, TestContext.Current.CancellationToken);

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
        await run.RunAsync(items, TestContext.Current.CancellationToken);
        return (items, disk);
    }

    [Fact]
    public async Task RewriteAllReplacesEverySavedFile()
    {
        var (items, disk) = await TranscribedAsync(@"C:\a.mp3", @"C:\b.mp3");

        await FileTranscriptionRun.RewriteAllAsync(items, text => text.ToUpperInvariant(), disk.Overwrite, TestContext.Current.CancellationToken);

        Assert.Equal(("raw of " + items[0].Path).ToUpperInvariant(), disk.Files[@"C:\a.mp3.txt"]);
        Assert.Equal(("raw of " + items[1].Path).ToUpperInvariant(), items[1].Text);
        Assert.All(items, i => Assert.Equal(FileTranscriptionState.Done, i.State));
    }

    [Fact]
    public async Task RewriteAllLeavesAFailedFileAlone()
    {
        var (items, disk) = await TranscribedAsync(@"C:\bad.mp3", @"C:\good.mp3");
        var rewritten = new List<string>();

        await FileTranscriptionRun.RewriteAllAsync(items, text => { rewritten.Add(text); return "new"; }, disk.Overwrite,
            TestContext.Current.CancellationToken);

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
            items, _ => throws ? throw new HttpRequestException() : "", disk.Overwrite, TestContext.Current.CancellationToken);

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
        await FileTranscriptionRun.RewriteAllAsync(items, _ => "", disk.Overwrite, TestContext.Current.CancellationToken);

        await FileTranscriptionRun.RewriteAllAsync(items, _ => "fine", disk.Overwrite, TestContext.Current.CancellationToken);

        Assert.False(items[0].RewriteFailed);
        Assert.Equal("fine", disk.Files[@"C:\a.mp3.txt"]);
    }

    // Stop lets the file in progress finish and gives up the ones that wait:
    // the engine cannot drop a file halfway, and a file never started was
    // never transcribed.
    [Fact]
    public async Task StopLetsTheCurrentFileFinishAndMarksTheWaitingOnesStopped()
    {
        var disk = new Disk();
        using var stop = new CancellationTokenSource();
        var run = new FileTranscriptionRun(
            path => { stop.Cancel(); return Task.FromResult<string?>("text of " + path); },
            null, disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3", @"C:\b.mp3", @"C:\c.mp3");

        await run.RunAsync(items, stop.Token);

        Assert.Equal(FileTranscriptionState.Done, items[0].State);
        Assert.Equal(@"text of C:\a.mp3", disk.Files[@"C:\a.mp3.txt"]);
        Assert.Equal(FileTranscriptionState.Stopped, items[1].State);
        Assert.Equal(FileTranscriptionState.Stopped, items[2].State);
        Assert.False(disk.Files.ContainsKey(@"C:\b.mp3.txt"));
    }

    // A file chosen while the batch runs is read live and transcribed in its turn.
    [Fact]
    public async Task AFileAddedDuringTheRunIsTranscribed()
    {
        var disk = new Disk();
        List<FileTranscriptionItem> items = [.. Items(@"C:\a.mp3")];
        var run = new FileTranscriptionRun(
            path =>
            {
                if (path.EndsWith("a.mp3")) items.Add(new FileTranscriptionItem(@"C:\late.mp3"));
                return Task.FromResult<string?>("text");
            },
            null, disk.Write, disk.Overwrite);

        await run.RunAsync(items, TestContext.Current.CancellationToken);

        Assert.Equal(2, items.Count);
        Assert.Equal(FileTranscriptionState.Done, items[1].State);
        Assert.True(disk.Files.ContainsKey(@"C:\late.mp3.txt"));
    }

    // A file already done or already failed is not run again by a later pass.
    [Fact]
    public async Task AFileThatIsNotWaitingIsSkipped()
    {
        var transcribed = new List<string>();
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            path => { transcribed.Add(path); return Task.FromResult<string?>("text"); },
            null, disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\done.mp3", @"C:\failed.mp3", @"C:\stopped.mp3", @"C:\new.mp3");
        items[0].State = FileTranscriptionState.Done;
        items[1].State = FileTranscriptionState.TranscriptionFailed;
        items[2].State = FileTranscriptionState.Stopped;

        await run.RunAsync(items, TestContext.Current.CancellationToken);

        Assert.Equal([@"C:\new.mp3"], transcribed);
        Assert.Equal(FileTranscriptionState.TranscriptionFailed, items[1].State);
        Assert.Equal(FileTranscriptionState.Stopped, items[2].State);
    }

    // A stopped file chosen again goes back to Waiting and the next pass takes it.
    [Fact]
    public async Task AStoppedFileSetBackToWaitingIsTranscribed()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("text"), null, disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3");
        items[0].State = FileTranscriptionState.Stopped;
        items[0].State = FileTranscriptionState.Waiting;

        await run.RunAsync(items, TestContext.Current.CancellationToken);

        Assert.Equal(FileTranscriptionState.Done, items[0].State);
    }

    // The mark that tells a rewritten text from the raw transcript.
    [Fact]
    public async Task ASuccessfulRewriteMarksTheFileRewritten()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"), text => text.ToUpperInvariant(), disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3");

        await run.RunAsync(items, TestContext.Current.CancellationToken);

        Assert.True(items[0].Rewritten);
    }

    [Fact]
    public async Task AFailedRewriteLeavesTheFileNotRewritten()
    {
        var disk = new Disk();
        var run = new FileTranscriptionRun(
            _ => Task.FromResult<string?>("raw"), _ => "", disk.Write, disk.Overwrite);
        FileTranscriptionItem[] items = Items(@"C:\a.mp3");

        await run.RunAsync(items, TestContext.Current.CancellationToken);

        Assert.False(items[0].Rewritten);
        Assert.True(items[0].RewriteFailed);
    }

    [Fact]
    public async Task RewriteAllMarksEverySavedFileRewritten()
    {
        var (items, disk) = await TranscribedAsync(@"C:\a.mp3", @"C:\b.mp3");

        await FileTranscriptionRun.RewriteAllAsync(items, text => text.ToUpperInvariant(), disk.Overwrite, TestContext.Current.CancellationToken);

        Assert.All(items, i => Assert.True(i.Rewritten));
    }

    // Stop between two files: the one being rewritten finishes, the next is left as it is.
    [Fact]
    public async Task RewriteAllHonoursStop()
    {
        var (items, disk) = await TranscribedAsync(@"C:\a.mp3", @"C:\b.mp3", @"C:\c.mp3");
        using var stop = new CancellationTokenSource();

        await FileTranscriptionRun.RewriteAllAsync(
            items, text => { stop.Cancel(); return text.ToUpperInvariant(); }, disk.Overwrite, stop.Token);

        Assert.Equal(("raw of " + items[0].Path).ToUpperInvariant(), disk.Files[@"C:\a.mp3.txt"]);
        Assert.True(items[0].Rewritten);
        Assert.Equal(@"raw of C:\b.mp3", disk.Files[@"C:\b.mp3.txt"]);
        Assert.False(items[1].Rewritten);
        Assert.False(items[2].Rewritten);
        Assert.All(items, i => Assert.Equal(FileTranscriptionState.Done, i.State));
    }
}