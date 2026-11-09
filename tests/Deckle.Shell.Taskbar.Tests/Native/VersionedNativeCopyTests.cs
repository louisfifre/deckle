using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// Uses a real file with a version resource (the module's own assembly) as the
// stand-in for the native DLL: the logic under test reads its bytes and its
// file version, not what it exports.
[Trait("Category", "integration")]
public sealed class VersionedNativeCopyTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "Deckle.Shell.Taskbar.Tests", "versioned-" + Guid.NewGuid().ToString("N"));

    public VersionedNativeCopyTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string MakeSource(string name, byte[]? extraBytes = null)
    {
        string path = Path.Combine(_folder, name);
        File.Copy(typeof(VersionedNativeCopy).Assembly.Location, path);
        if (extraBytes is not null)
        {
            // Trailing bytes change the content hash and leave the version resource alone.
            using var stream = new FileStream(path, FileMode.Append);
            stream.Write(extraBytes);
        }
        return path;
    }

    private VersionedNativeCopy NewCopy() => new(Path.Combine(_folder, "root"));

    [Fact]
    public void TheFolderNameIsTheFileVersionAndEightHexOfTheContentHash()
    {
        string name = VersionedNativeCopy.FolderNameFor(MakeSource("a.dll"));

        Assert.Matches(@"^\d+(\.\d+)+-[0-9a-f]{8}$", name);
    }

    [Fact]
    public void EnsureCopiesTheDllIntoItsVersionedFolder()
    {
        string source = MakeSource("a.dll");
        VersionedNativeCopy copy = NewCopy();

        string path = copy.Ensure(source);

        Assert.Equal(VersionedNativeCopy.DllName, Path.GetFileName(path));
        Assert.Equal(VersionedNativeCopy.FolderNameFor(source), Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(path));
    }

    [Fact]
    public void EnsureTwiceReturnsTheSamePathAndLeavesNoTemporaryFile()
    {
        string source = MakeSource("a.dll");
        VersionedNativeCopy copy = NewCopy();

        string first = copy.Ensure(source);
        string second = copy.Ensure(source);

        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(first)!));
    }

    // A development build keeps its file version while its bytes change; the
    // hash in the name gives it a new path, so a running explorer's lock on
    // the old file does not block it.
    [Fact]
    public void ADllWithTheSameVersionAndNewBytesGetsANewPath()
    {
        VersionedNativeCopy copy = NewCopy();

        string old = copy.Ensure(MakeSource("old.dll"));
        string rebuilt = copy.Ensure(MakeSource("new.dll", extraBytes: [1, 2, 3]));

        Assert.NotEqual(old, rebuilt);
        Assert.True(File.Exists(old));
        Assert.True(File.Exists(rebuilt));
    }

    [Fact]
    public void SweepRemovesEveryOtherVersionAndKeepsTheOneNamed()
    {
        VersionedNativeCopy copy = NewCopy();
        string old = copy.Ensure(MakeSource("old.dll"));
        string current = copy.Ensure(MakeSource("new.dll", extraBytes: [9]));

        IReadOnlyList<string> removed = copy.Sweep(current);

        Assert.Equal([Path.GetDirectoryName(old)!], removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(current));
    }

    // Explorer keeps the DLL it loaded open: its folder cannot be deleted, and
    // the sweep goes on without throwing so the next one can try again.
    [Fact]
    public void SweepLeavesAFolderWhoseFileIsHeldOpenAndDoesNotThrow()
    {
        VersionedNativeCopy copy = NewCopy();
        string locked = copy.Ensure(MakeSource("old.dll"));
        string current = copy.Ensure(MakeSource("new.dll", extraBytes: [9]));

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            IReadOnlyList<string> removed = copy.Sweep(current);

            Assert.Empty(removed);
            Assert.True(File.Exists(locked));
        }

        Assert.Single(copy.Sweep(current));   // released: the next sweep removes it
    }

    [Fact]
    public void SweepOfARootThatDoesNotExistYetRemovesNothing()
    {
        Assert.Empty(NewCopy().Sweep(Path.Combine(_folder, "root", "x", VersionedNativeCopy.DllName)));
    }
}
