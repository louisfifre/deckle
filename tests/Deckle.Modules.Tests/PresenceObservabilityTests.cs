using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Modules.Tests;

// What the journal says when the presence file cannot be used: every module then
// counts as present, which is a decision the person did not make, so the line
// has to name the cause and nothing the file holds.
[Trait("Category", "observability")]
public sealed class PresenceObservabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "Deckle-Tests", Path.GetRandomFileName());

    private string FilePath => Path.Combine(_dir, "presence.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    [Fact]
    [Trait("Category", "regression")]
    public void ACorruptPresenceFileIsAWarningInTheJournalNamingTheErrorTypeAndNoPath()
    {
        using var listener = new TestEventListener("Deckle-Modules");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Null(PresenceFile.LoadFrom(FilePath));

        EventWrittenEventArgs warning = Assert.Single(
            listener.Events, e => e.EventId == DeckleModulesSource.EvtPresenceLoadFailed);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.Equal("JsonException", Field(warning, "reason"));
        Assert.DoesNotContain(
            listener.Events,
            e => e.Payload!.Any(p => Convert.ToString(p)?.Contains(_dir, StringComparison.OrdinalIgnoreCase) == true));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AValidFileWithoutAPresentListIsAWarningNotASilentNoChoice()
    {
        using var listener = new TestEventListener("Deckle-Modules");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"version\": 2, \"known\": [\"ambient\"] }");

        Assert.Null(PresenceFile.LoadFrom(FilePath));

        EventWrittenEventArgs warning = Assert.Single(
            listener.Events, e => e.EventId == DeckleModulesSource.EvtPresenceLoadFailed);
        Assert.Equal(
            DeckleModulesSource.PresenceLoadFailureReasons.PresentListMissing,
            Field(warning, "reason"));
    }

    [Fact]
    public void AMissingFileIsNoChoiceAndSaysNothingAboutAFailure()
    {
        using var listener = new TestEventListener("Deckle-Modules");

        Assert.Null(PresenceFile.LoadFrom(FilePath));

        Assert.DoesNotContain(
            listener.Events, e => e.EventId == DeckleModulesSource.EvtPresenceLoadFailed);
    }
}
