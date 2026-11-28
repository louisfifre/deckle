using Xunit;

namespace Deckle.FolderMap.Tests;

// The map builder emits on the process-wide Deckle-FolderMap source, and the
// events tests count what it writes, so every class that builds a map shares
// this collection and runs one after the other. No class outside it emits on
// that source, so the collection keeps running beside the rest.
[CollectionDefinition(Name)]
public sealed class FolderMapProviderCollection
{
    public const string Name = "foldermap provider singleton";
}
