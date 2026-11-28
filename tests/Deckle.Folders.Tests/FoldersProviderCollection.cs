using Xunit;

namespace Deckle.Folders.Tests;

// The listing, the store, the scans and the photo services emit on the
// process-wide Deckle-Folders source, and the events tests count what they
// write, so every class that calls them shares this collection and runs one
// after the other. No class outside it emits on that source, so the collection
// keeps running beside the rest.
[CollectionDefinition(Name)]
public sealed class FoldersProviderCollection
{
    public const string Name = "folders provider singleton";
}
