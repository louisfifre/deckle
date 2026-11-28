using Xunit;

namespace Deckle.Notifications.Tests;

// The dispatcher is a process-wide singleton, and the catalogue, the dispatcher
// and the toast incidents emit on the process-wide Deckle-Notifications source
// that the observability tests count. Every class that registers, dispatches or
// listens shares this collection and runs one after the other. No class outside
// it emits on that source, so the collection keeps running beside the rest.
[CollectionDefinition(Name)]
public sealed class NotificationDispatcherCollection
{
    public const string Name = "notification dispatcher singleton";
}
