using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The approved-families store — per-user data with the personal dictionary's
// discipline. These pin the tolerance contract: a missing or corrupt file is
// an empty set (the corrector stays inert), an unreadable record is skipped,
// never a boot failure.
[Trait("Category", "unit")]
public class MistouchFamilyStoreTests
{
    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void LoadsApprovedFamiliesWithTheirParameters()
    {
        string path = WriteTemp("""
            [
              { "signature": "sub ;→'", "kind": "boundary_apostrophe" },
              { "signature": "dropped space after ,", "kind": "boundary_missing_space", "punctuation": "," }
            ]
            """);
        try
        {
            var records = MistouchFamilyStore.Load(path);

            Assert.Equal(2, records.Count);
            Assert.Equal(MistouchFamilyKinds.BoundaryApostrophe, records[0].Kind);
            Assert.Equal(",", records[1].Punctuation);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMissingFileIsAnEmptySet()
    {
        Assert.Empty(MistouchFamilyStore.Load(
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())));
    }

    [Fact]
    public void ACorruptFileIsAnEmptySetNeverAThrow()
    {
        string path = WriteTemp("{ not json ]");
        try { Assert.Empty(MistouchFamilyStore.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnUnreadableRecordIsSkippedTheRestSurvives()
    {
        string path = WriteTemp("""
            [
              { "kind": "boundary_apostrophe" },
              { "signature": "sub ;→'", "kind": "boundary_apostrophe" }
            ]
            """);
        try
        {
            var records = MistouchFamilyStore.Load(path);
            Assert.Single(records);
        }
        finally { File.Delete(path); }
    }
}
