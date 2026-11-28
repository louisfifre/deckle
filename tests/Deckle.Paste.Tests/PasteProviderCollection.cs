using Xunit;

namespace Deckle.Paste.Tests;

// The folder store and the saver emit on the process-wide Deckle-Paste source
// that the events tests count, so every class that calls them shares this
// collection and runs one after the other. No class outside it emits on that
// source, so the collection keeps running beside the rest.
[CollectionDefinition(Name)]
public sealed class PasteProviderCollection
{
    public const string Name = "paste provider singleton";
}
