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
    public async Task AnImageFileHasAThumbnailOfItsContent()
    {
        RequireOptIn();
        using var scratch = new ScratchFolder();
        string bmp = Path.Combine(scratch.Path, "red.bmp");
        File.WriteAllBytes(bmp, RedBitmap(64));
        using var queue = new ShellWorkQueue("test");

        ShellImage? thumbnail = await queue.RunAsync(() => ShellImageReader.ExtractThumbnail(bmp, 48), CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.True(thumbnail.Width >= 48 || thumbnail.Height >= 48);
        // The centre pixel is the image's red, not an icon's: B, G, R, A.
        int centre = ((thumbnail.Height / 2) * thumbnail.Width + thumbnail.Width / 2) * 4;
        Assert.True(thumbnail.Bgra[centre + 2] > 200 && thumbnail.Bgra[centre + 1] < 50 && thumbnail.Bgra[centre] < 50);
    }

    // A size x size, 24-bit, bottom-up BMP filled with pure red.
    private static byte[] RedBitmap(int size)
    {
        int row = (size * 3 + 3) & ~3;
        int pixels = row * size;
        var data = new byte[54 + pixels];
        data[0] = (byte)'B'; data[1] = (byte)'M';
        BitConverter.GetBytes(data.Length).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(size).CopyTo(data, 18);
        BitConverter.GetBytes(size).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        BitConverter.GetBytes(pixels).CopyTo(data, 34);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                data[54 + y * row + x * 3 + 2] = 255;
        return data;
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
