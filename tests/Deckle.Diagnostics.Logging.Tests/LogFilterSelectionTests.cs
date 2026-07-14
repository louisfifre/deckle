using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Logging.Tests;

public sealed class LogFilterSelectionTests
{
    [Fact]
    public void EmptySelectionMatchesEveryEntry()
    {
        var selection = new LogFilterSelection();

        Assert.True(selection.Matches(Entry(
            "Deckle-Vision", EventLevel.Verbose, Keywords.Capture)));
    }

    [Fact]
    public void ValuesInsideOneDimensionAreOrCombined()
    {
        var selection = new LogFilterSelection();
        selection.Add(Severity(EventLevel.Warning));
        selection.Add(Severity(EventLevel.Error));

        Assert.True(selection.Matches(Entry(
            "Deckle-Vision", EventLevel.Warning, Keywords.Pipeline)));
        Assert.True(selection.Matches(Entry(
            "Deckle-Vision", EventLevel.Error, Keywords.Pipeline)));
        Assert.False(selection.Matches(Entry(
            "Deckle-Vision", EventLevel.Informational, Keywords.Pipeline)));
    }

    [Fact]
    public void ActiveDimensionsAreAndCombined()
    {
        var selection = new LogFilterSelection();
        selection.Add(Severity(EventLevel.Warning));
        selection.Add(new LogFilterToken(LogFilterDimension.Module, "Deckle-Vision"));

        Assert.True(selection.Matches(Entry(
            "Deckle-Vision", EventLevel.Warning, Keywords.Capture)));
        Assert.False(selection.Matches(Entry(
            "Deckle-Audio", EventLevel.Warning, Keywords.Capture)));
        Assert.False(selection.Matches(Entry(
            "Deckle-Vision", EventLevel.Error, Keywords.Capture)));
    }

    [Fact]
    public void CategoryMatchesAnySelectedTransverseKeyword()
    {
        var selection = new LogFilterSelection();
        selection.Add(Category(Keywords.Capture));
        selection.Add(Category(Keywords.Heartbeat));

        Assert.True(selection.Matches(Entry(
            "Deckle-Audio", EventLevel.Verbose, Keywords.Capture | Keywords.Pipeline)));
        Assert.True(selection.Matches(Entry(
            "Deckle-Audio", EventLevel.Verbose, Keywords.Heartbeat)));
        Assert.False(selection.Matches(Entry(
            "Deckle-Audio", EventLevel.Verbose, Keywords.Lifecycle)));

        selection.Remove(Category(Keywords.Capture));

        Assert.False(selection.Matches(Entry(
            "Deckle-Audio", EventLevel.Verbose, Keywords.Capture)));
        Assert.True(selection.Matches(Entry(
            "Deckle-Audio", EventLevel.Verbose, Keywords.Heartbeat)));
    }

    [Fact]
    public void RemovingLastTokenRestoresTheUnfilteredView()
    {
        var selection = new LogFilterSelection();
        LogFilterToken token = Severity(EventLevel.Critical);
        selection.Add(token);

        selection.Remove(token);

        Assert.True(selection.IsEmpty);
        Assert.True(selection.Matches(Entry(
            "Deckle-App", EventLevel.Informational, Keywords.Lifecycle)));
    }

    [Fact]
    public void LogAlwaysUsesInformationalSeveritySemantics()
    {
        var selection = new LogFilterSelection();
        selection.Add(Severity(EventLevel.Informational));

        Assert.True(selection.Matches(Entry(
            "Deckle-App", EventLevel.LogAlways, Keywords.Lifecycle)));
    }

    [Fact]
    public void ChangedIsRaisedOnlyForEffectiveMutations()
    {
        var selection = new LogFilterSelection();
        LogFilterToken token = Severity(EventLevel.Warning);
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        selection.Add(token);
        selection.Add(token);
        selection.Remove(Severity(EventLevel.Error));
        selection.Remove(token);
        selection.Clear();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void ClearRemovesEveryDimensionWithOneNotification()
    {
        var selection = new LogFilterSelection();
        selection.Add(Severity(EventLevel.Warning));
        selection.Add(new LogFilterToken(LogFilterDimension.Module, "Deckle-Vision"));
        selection.Add(Category(Keywords.Capture));
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        selection.Clear();

        Assert.True(selection.IsEmpty);
        Assert.Empty(selection.GetTokens());
        Assert.Equal(1, changes);
    }

    private static LogFilterToken Severity(EventLevel level)
        => new(LogFilterDimension.Severity, level.ToString());

    private static LogFilterToken Category(Keywords category)
        => new(LogFilterDimension.Category, category.ToString());

    private static EventEntry Entry(
        string provider,
        EventLevel level,
        Keywords keywords)
        => new(
            DateTimeOffset.UtcNow,
            provider,
            "TestEvent",
            level,
            (EventKeywords)keywords,
            "Test event",
            new Dictionary<string, object?>());
}
