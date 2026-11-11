using Xunit;

namespace Deckle.Atelier.Tests;

// The text mode of the address bar: a typed path leads inside an added folder
// or nowhere, and suggestions complete what is typed.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class AddressResolverTests
{
    [Fact]
    public void ATypedPathInsideAnAddedFolderLeadsThere()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string photos = scratch.Folder("Photos");
        string y2026 = scratch.Folder("Photos", "2026");
        store.Add(photos);

        AddressResolution resolution = AddressResolver.Resolve($"  \"{y2026}\\\" ", store);

        Assert.Equal(AddressFailure.None, resolution.Failure);
        Assert.Equal("2026", resolution.Place!.Name);
        Assert.Equal("Photos", resolution.Place.Root.Name);
    }

    [Fact]
    public void APathOutsideTheAddedFoldersIsRefused()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        store.Add(scratch.Folder("Photos"));

        Assert.Equal(AddressFailure.OutsideAddedFolders, AddressResolver.Resolve(scratch.Folder("Other"), store).Failure);
        Assert.Equal(AddressFailure.NotFound, AddressResolver.Resolve(Path.Combine(scratch.Path, "Photos", "gone"), store).Failure);
        Assert.Equal(AddressFailure.Empty, AddressResolver.Resolve("  ", store).Failure);
    }

    [Fact]
    public void SuggestionsCompleteTheLastTypedPart()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string photos = scratch.Folder("Photos");
        scratch.Folder("Photos", "2026-06-14 Mariage");
        scratch.Folder("Photos", "2026-06-21 Poterie");
        scratch.Folder("Photos", "2025");
        store.Add(photos);

        IReadOnlyList<string> suggestions = AddressResolver.Suggestions(Path.Combine(photos, "2026-06"), store);

        Assert.Equal(["2026-06-14 Mariage", "2026-06-21 Poterie"], suggestions.Select(Path.GetFileName));
    }

    [Fact]
    public void AboveTheAddedFoldersTheSuggestionsAreTheAddedFolders()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string photos = scratch.Folder("Photos");
        store.Add(photos);

        Assert.Equal([FolderPaths.Normalize(photos)], AddressResolver.Suggestions(Path.Combine(scratch.Path, "Pho"), store));
    }
}
