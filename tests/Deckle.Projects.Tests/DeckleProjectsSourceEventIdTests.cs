using System.Diagnostics.Tracing;
using System.Reflection;
using Xunit;

namespace Deckle.Projects.Tests;

// Parallel work on a provider's partial files can give two events one id and
// git merges them without a conflict because they sit in different files. An
// EventSource with a duplicate id cannot build its event table, so the whole
// Deckle-Projects provider goes silent. Deckle Folders met it first
// (Deckle.Folders.Tests/FoldersEventIdBugNotes.md): this test reads the [Event]
// attributes the runtime reads and names the clash.
[Trait("Category", "regression")]
public sealed class DeckleProjectsSourceEventIdTests
{
    [Fact]
    public void EveryProjectsEventHasItsOwnId()
    {
        var clashes = typeof(DeckleProjectsSource)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => (Method: m.Name, Attribute: m.GetCustomAttribute<EventAttribute>()))
            .Where(e => e.Attribute is not null)
            .GroupBy(e => e.Attribute!.EventId)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Method))}")
            .ToArray();

        Assert.Empty(clashes);
    }

    // The path rules the provider promises to every window of the app: no
    // event of the provider names a path, a project or an action. A payload
    // parameter with one of these names would say it.
    [Fact]
    public void NoEventPayloadIsNamedAfterAPathAProjectOrAnAction()
    {
        string[] forbidden = ["path", "name", "folder", "project", "action", "label", "script", "url", "remote", "title"];
        var offenders = typeof(DeckleProjectsSource)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<EventAttribute>() is not null)
            .SelectMany(m => m.GetParameters().Select(p => (Method: m.Name, Parameter: p.Name!)))
            .Where(p => forbidden.Contains(p.Parameter, StringComparer.OrdinalIgnoreCase))
            .Select(p => $"{p.Method}({p.Parameter})")
            .ToArray();

        Assert.Empty(offenders);
    }
}
