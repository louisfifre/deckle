using Xunit;

namespace Deckle.Atelier.Tests;

// The reader against this machine's real shell: the icons of a folder and of
// a few types come back at the size asked, distinct from one another, and a
// file the shell cannot reach answers null. Opt-in, because what the shell
// draws depends on the machine's installed handlers: set
// DECKLE_ATELIER_RUN_SYSTEM=1.
[Trait("Category", "system")]
public sealed class ShellImageReaderSystemTests
{
    private static void RequireOptIn() => Assert.SkipUnless(
        string.Equals(Environment.GetEnvironmentVariable("DECKLE_ATELIER_RUN_SYSTEM"), "1", StringComparison.Ordinal),
        "Set DECKLE_ATELIER_RUN_SYSTEM=1 to run the shell image system tests.");

    [Fact]
    public async Task TheIconsOfAFolderAndOfThreeTypesComeBackDistinctAtTheSizeAsked()
    {
        RequireOptIn();
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("Sub");
        string md = scratch.File("# Note", "note.md");
        string zip = scratch.File("", "archive.zip");
        string txt = scratch.File("text", "plain.txt");
        using var queue = new ShellWorkQueue("test");

        ShellImage?[] icons = await Task.WhenAll(
            new[] { folder, md, zip, txt }.Select(path =>
                queue.RunAsync(() => ShellImageReader.ReadIcon(path, 20), CancellationToken.None)));

        Assert.All(icons, icon =>
        {
            Assert.NotNull(icon);
            Assert.Equal(20, icon.Width);
            Assert.Equal(20, icon.Height);
            Assert.Contains(icon.Bgra.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
        });
        Assert.Equal(icons.Length, icons.Select(i => Convert.ToBase64String(i!.Bgra)).Distinct().Count());
    }

    [Fact]
    public async Task AFileTheShellCannotReachAnswersNull()
    {
        RequireOptIn();
        using var queue = new ShellWorkQueue("test");
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "gone.md");

        Assert.Null(await queue.RunAsync(() => ShellImageReader.ReadIcon(missing, 20), CancellationToken.None));
        Assert.Null(await queue.RunAsync(() => ShellImageReader.ReadCachedThumbnail(missing, 256), CancellationToken.None));
    }

    [Fact]
    public async Task AFileWithoutAPictureHasNoThumbnail()
    {
        RequireOptIn();
        using var scratch = new ScratchFolder();
        string txt = scratch.File("text", "plain.txt");
        using var queue = new ShellWorkQueue("test");

        Assert.Null(await queue.RunAsync(() => ShellImageReader.ExtractThumbnail(txt, 256), CancellationToken.None));
    }
}
