using Xunit;

namespace Deckle.Projects.Tests;

// `git status --porcelain=v1 -z` gives "XY path" entries ending in NUL; a
// rename carries its origin as one more field that is not an entry.
[Trait("Category", "unit")]
public sealed class UncommittedFilesTests
{
    private static string Z(params string[] fields) => string.Concat(fields.Select(f => f + "\0"));

    [Fact]
    public void NothingReadsAsZero()
    {
        Assert.Equal(0, UncommittedFiles.Count(""));
    }

    [Fact]
    public void ModifiedAddedAndUntrackedEntriesAreOneEachWithTheirSpacesAndNonAscii()
    {
        string output = Z(" M src/main.rs", "A  new file.txt", "?? notes/écrit ça.md");

        Assert.Equal(3, UncommittedFiles.Count(output));
    }

    [Fact]
    public void ARenameIsOneEntryNotTwo()
    {
        string output = Z("R  new name.txt", "old name.txt", " M other.txt", "RM moved.txt", "was.txt");

        Assert.Equal(3, UncommittedFiles.Count(output));
    }

    [Fact]
    public void ACopyIsOneEntryToo()
    {
        Assert.Equal(1, UncommittedFiles.Count(Z("C  copy.txt", "source.txt")));
    }

    [Fact]
    public void ADeletedAndAConflictedFileCount()
    {
        Assert.Equal(2, UncommittedFiles.Count(Z(" D gone.txt", "UU conflict.txt")));
    }
}
